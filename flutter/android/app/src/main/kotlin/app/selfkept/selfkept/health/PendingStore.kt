package app.selfkept.selfkept.health

import android.content.Context
import com.google.gson.stream.JsonReader
import com.google.gson.stream.JsonToken
import com.google.gson.stream.JsonWriter
import java.io.File
import java.io.FileOutputStream
import java.io.FilterReader
import java.io.Reader
import java.util.concurrent.atomic.AtomicLong

/**
 * Rows the background worker collected while Dart was not running, waiting for
 * the next `collectSince` to hand them to Dart.
 *
 * Layout: one directory of JSON objects `{ "samples": [...], "workouts": [...] }`.
 * The worker writes `<millis>-0-<n>.json`; a split writes `<millis>-n<stamp>-<n>.json`
 * with the millis of the file it came from. Files sort by millis, then by
 * descending stamp (a split always comes from the first file, so its chunks
 * must stay first), then by n. Older builds wrote a single `<millis>.json`
 * per worker run with no size limit; those are read the same way.
 *
 * Memory is bounded on both sides:
 * - [write] streams rows to disk in chunks of at most [CHUNK_ROWS] rows.
 * - [drain] streams files oldest first and stops at a row budget and a
 *   character budget. A file it only partly read has its unread rows copied
 *   to new chunk files (written to `.tmp` and renamed) before the original is
 *   deleted, so a crash at any point leaves either the old file or the new
 *   chunks, never neither. A row read twice is harmless: the server
 *   deduplicates on sourceUid. A row lost is not, so nothing is deleted until
 *   its rows are either returned or safely rewritten.
 *
 * Plain JVM code (java.io and Gson's streaming reader) so it runs in unit tests.
 */
class PendingStore(val dir: File) {

    companion object {
        const val DIR = "health_pending"

        /** Rows per file written by [write] or by a split. */
        const val CHUNK_ROWS = 1000

        /** JSON characters one [drain] may read; about 4 MB. */
        const val DRAIN_CHARS: Long = 4L * 1024 * 1024

        /** The worker stops staging once the folder holds this many files... */
        const val MAX_FILES = 200

        /** ...or this many bytes. */
        const val MAX_BYTES: Long = 8L * 1024 * 1024

        /** One lock for every instance: the worker and the plugin share a process. */
        private val LOCK = Any()
        private val tagCounter = AtomicLong(0)

        fun of(context: Context): PendingStore = PendingStore(File(context.filesDir, DIR))
    }

    class Drained(val samples: List<Any?>, val workouts: List<Any?>, val moreLeft: Boolean) {
        val size: Int get() = samples.size + workouts.size
    }

    class Stats(val files: Int, val bytes: Long)

    private enum class Outcome { COMPLETE, PARTIAL, WRITE_FAILED }

    fun stats(): Stats = synchronized(LOCK) {
        val files = pendingFiles()
        Stats(files.size, files.sumOf { it.length() })
    }

    /** True when the worker should stop adding rows until Dart catches up. */
    fun isFull(): Boolean {
        val s = stats()
        return s.files >= MAX_FILES || s.bytes >= MAX_BYTES
    }

    /**
     * Stages rows for the next drain. Returns false when they could not be
     * written; nothing partial is left behind in that case, and the caller must
     * not advance its Health Connect tokens.
     */
    fun write(samples: List<Any?>, workouts: List<Any?>, now: Long = System.currentTimeMillis()): Boolean =
        synchronized(LOCK) {
            if (samples.isEmpty() && workouts.isEmpty()) return true
            dir.mkdirs()
            var millis = now
            while (File(dir, "$millis-0-0.json").exists()) millis += 1
            val out = ChunkWriter(dir, millis.toString(), "0")
            for (row in samples) out.row("samples", row)
            for (row in workouts) out.row("workouts", row)
            out.commit()
        }

    /**
     * Takes at most [maxRows] rows (samples and workouts together) and roughly
     * [maxChars] characters of JSON, oldest file first. At least one row is
     * taken when any is available, so every call makes progress.
     */
    fun drain(maxRows: Int, maxChars: Long = DRAIN_CHARS): Drained = synchronized(LOCK) {
        removeStaleTemps()
        val samples = ArrayList<Any?>()
        val workouts = ArrayList<Any?>()
        var chars = 0L
        var moreLeft = false
        for (file in pendingFiles()) {
            val rowsLeft = maxRows - samples.size - workouts.size
            if (rowsLeft <= 0 || chars >= maxChars) {
                moreLeft = true
                break
            }
            val reader = CountingReader(file.bufferedReader(Charsets.UTF_8))
            val outcome = reader.use { consume(file, it, rowsLeft, maxChars - chars, samples, workouts) }
            chars += reader.count
            when (outcome) {
                Outcome.COMPLETE -> file.delete()
                Outcome.PARTIAL -> {
                    // The unread rows now live in new chunk files. Deleted only
                    // after the reader is closed (Windows refuses otherwise).
                    file.delete()
                    moreLeft = true
                    break
                }
                Outcome.WRITE_FAILED -> {
                    // The original stays; its rows are read again next time.
                    // Stop here so the caller does not spin on the same rows.
                    break
                }
            }
        }
        Drained(samples, workouts, moreLeft)
    }

    // ---------------------------------------------------------------- internals

    /** Grows with every split, so a newer split sorts ahead of an older one. */
    private fun splitTag(): String =
        "n" + (System.currentTimeMillis() * 1000 + tagCounter.incrementAndGet() % 1000)

    private fun sortKey(file: File): Triple<Long, Long, Long> {
        val parts = file.name.removeSuffix(".json").split('-')
        val millis = parts[0].toLongOrNull() ?: Long.MAX_VALUE
        val stamp = parts.getOrNull(1)?.removePrefix("n")?.toLongOrNull() ?: 0L
        val n = parts.getOrNull(2)?.toLongOrNull() ?: 0L
        return Triple(millis, -stamp, n)
    }

    /** Oldest first; see the class comment for the order inside one millis. */
    private fun pendingFiles(): List<File> {
        val files = dir.listFiles { f -> f.isFile && f.name.endsWith(".json") } ?: return emptyList()
        return files.sortedWith(
            compareBy<File>({ sortKey(it).first }, { sortKey(it).second }, { sortKey(it).third }, { it.name })
        )
    }

    /** A `.tmp` only survives a crash mid-write; its original (if any) is still there. */
    private fun removeStaleTemps() {
        dir.listFiles { f -> f.isFile && f.name.endsWith(".tmp") }?.forEach { it.delete() }
    }

    private fun consume(
        file: File,
        counter: CountingReader,
        rowLimit: Int,
        charLimit: Long,
        samples: MutableList<Any?>,
        workouts: MutableList<Any?>,
    ): Outcome {
        val reader = JsonReader(counter)
        var taken = 0
        try {
            reader.beginObject()
            while (reader.hasNext()) {
                val name = reader.nextName()
                val target = when (name) {
                    "samples" -> samples
                    "workouts" -> workouts
                    else -> null
                }
                if (target == null || reader.peek() != JsonToken.BEGIN_ARRAY) {
                    reader.skipValue()
                    continue
                }
                reader.beginArray()
                while (reader.hasNext()) {
                    if (taken >= rowLimit || (taken > 0 && counter.count >= charLimit)) {
                        return splitRest(file, reader, name)
                    }
                    target.add(readValue(reader))
                    taken += 1
                }
                reader.endArray()
            }
            reader.endObject()
        } catch (_: Exception) {
            // Damaged (truncated or not JSON): the rows read before the damage
            // are kept, the rest of the file cannot be read and is dropped
            // rather than blocking every sync.
        }
        return Outcome.COMPLETE
    }

    /**
     * Copies every unread row of [file] (the rest of the array [current] is in,
     * then any later arrays) into new chunk files; the caller then deletes [file].
     */
    private fun splitRest(file: File, reader: JsonReader, current: String): Outcome {
        val base = file.name.substringBefore('-').removeSuffix(".json")
        val out = ChunkWriter(dir, base, splitTag())
        try {
            while (reader.hasNext()) out.row(current, readValue(reader))
            reader.endArray()
            while (reader.hasNext()) {
                val name = reader.nextName()
                if ((name != "samples" && name != "workouts") || reader.peek() != JsonToken.BEGIN_ARRAY) {
                    reader.skipValue()
                    continue
                }
                reader.beginArray()
                while (reader.hasNext()) out.row(name, readValue(reader))
                reader.endArray()
            }
            reader.endObject()
        } catch (_: Exception) {
            // Damage after this point: keep every row copied so far.
        }
        return if (out.commit()) Outcome.PARTIAL else Outcome.WRITE_FAILED
    }

    /** One JSON value as plain maps, lists and scalars (what Flutter's codec takes). */
    private fun readValue(reader: JsonReader): Any? = when (reader.peek()) {
        JsonToken.BEGIN_OBJECT -> {
            val map = LinkedHashMap<String, Any?>()
            reader.beginObject()
            while (reader.hasNext()) map[reader.nextName()] = readValue(reader)
            reader.endObject()
            map
        }
        JsonToken.BEGIN_ARRAY -> {
            val list = ArrayList<Any?>()
            reader.beginArray()
            while (reader.hasNext()) list.add(readValue(reader))
            reader.endArray()
            list
        }
        JsonToken.STRING -> reader.nextString()
        JsonToken.NUMBER -> parseNumber(reader.nextString())
        JsonToken.BOOLEAN -> reader.nextBoolean()
        JsonToken.NULL -> {
            reader.nextNull()
            null
        }
        else -> throw IllegalStateException("Unexpected ${reader.peek()}")
    }

    /** Whole numbers stay whole (as org.json read them before), the rest are doubles. */
    private fun parseNumber(text: String): Any {
        if (text.none { it == '.' || it == 'e' || it == 'E' }) {
            val whole = text.toLongOrNull()
            if (whole != null) {
                return if (whole in Int.MIN_VALUE..Int.MAX_VALUE) whole.toInt() else whole
            }
        }
        return text.toDouble()
    }

    /** Counts characters handed to the JSON reader (it buffers about 1 KB ahead). */
    private class CountingReader(inner: Reader) : FilterReader(inner) {
        var count = 0L
            private set

        override fun read(): Int {
            val c = super.read()
            if (c >= 0) count += 1
            return c
        }

        override fun read(cbuf: CharArray, off: Int, len: Int): Int {
            val n = super.read(cbuf, off, len)
            if (n > 0) count += n
            return n
        }
    }

    /**
     * Writes rows into `<base>-<tag>-<n>.json.tmp` files of at most [CHUNK_ROWS]
     * rows each; [commit] syncs and renames them all, or removes them all.
     */
    private class ChunkWriter(private val dir: File, private val base: String, private val tag: String) {
        private val temps = mutableListOf<File>()
        private var stream: FileOutputStream? = null
        private var writer: JsonWriter? = null
        private var array: String? = null
        private var rows = 0
        private var failed = false

        fun row(arrayName: String, value: Any?) {
            if (failed) return
            try {
                if (writer == null || rows >= CHUNK_ROWS) {
                    closeChunk()
                    openChunk()
                }
                val w = writer!!
                if (array != arrayName) {
                    if (array != null) w.endArray()
                    w.name(arrayName).beginArray()
                    array = arrayName
                }
                writeValue(w, value)
                rows += 1
            } catch (_: Exception) {
                failed = true
            }
        }

        /** True when every chunk is in place (or there was nothing to write). */
        fun commit(): Boolean {
            if (!failed) {
                try {
                    closeChunk()
                } catch (_: Exception) {
                    failed = true
                }
            }
            if (failed) {
                discard()
                return false
            }
            for (tmp in temps) {
                if (!tmp.renameTo(File(dir, tmp.name.removeSuffix(".tmp")))) {
                    // Chunks already renamed stay: their rows also remain in the
                    // original, and a duplicate is harmless.
                    discard()
                    return false
                }
            }
            return true
        }

        private fun openChunk() {
            val tmp = File(dir, "$base-$tag-${temps.size}.json.tmp")
            temps.add(tmp)
            val s = FileOutputStream(tmp)
            stream = s
            writer = JsonWriter(s.bufferedWriter(Charsets.UTF_8)).also {
                it.serializeNulls = true
                it.beginObject()
            }
            array = null
            rows = 0
        }

        private fun closeChunk() {
            val w = writer ?: return
            writer = null
            try {
                if (array != null) w.endArray()
                w.endObject()
                w.flush()
                stream?.fd?.sync()
            } finally {
                w.close()
                stream = null
                array = null
            }
        }

        private fun discard() {
            try {
                writer?.close()
            } catch (_: Exception) {
            }
            writer = null
            stream = null
            for (tmp in temps) if (tmp.exists()) tmp.delete()
        }

        private fun writeValue(w: JsonWriter, value: Any?) {
            when (value) {
                null -> w.nullValue()
                is Map<*, *> -> {
                    w.beginObject()
                    for ((k, v) in value) {
                        w.name(k.toString())
                        writeValue(w, v)
                    }
                    w.endObject()
                }
                is List<*> -> {
                    w.beginArray()
                    for (v in value) writeValue(w, v)
                    w.endArray()
                }
                is String -> w.value(value)
                is Boolean -> w.value(value)
                is Number -> w.value(value)
                else -> w.value(value.toString())
            }
        }
    }
}
