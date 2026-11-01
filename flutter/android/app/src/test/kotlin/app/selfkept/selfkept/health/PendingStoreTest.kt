package app.selfkept.selfkept.health

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Rule
import org.junit.Test
import org.junit.rules.TemporaryFolder
import java.io.File

class PendingStoreTest {

    @get:Rule
    val tmp = TemporaryFolder()

    private lateinit var dir: File
    private lateinit var store: PendingStore

    @Before
    fun setUp() {
        dir = tmp.newFolder(PendingStore.DIR)
        store = PendingStore(dir)
    }

    // ------------------------------------------------------------ helpers

    private fun sample(uid: String): String =
        """{"sourceUid":"$uid","metric":"heart_rate_bpm","start":"2026-11-01T07:00:00Z","end":null,""" +
            """"value":61.5,"unit":"bpm","meta":{"hcType":"HeartRateRecord","sourcePackage":"com.google.android.apps.fitness"}}"""

    private fun workout(uid: String): String =
        """{"sourceUid":"$uid","type":"walking","start":"2026-11-01T07:00:00Z","end":"2026-11-01T07:30:00Z","distanceM":null}"""

    /** A file in the format the old worker wrote: one object, every row on one line. */
    private fun legacyFile(millis: Long, sampleUids: List<String>, workoutUids: List<String> = emptyList()): File {
        val body = """{"samples":[${sampleUids.joinToString(",") { sample(it) }}],""" +
            """"workouts":[${workoutUids.joinToString(",") { workout(it) }}]}"""
        return File(dir, "$millis.json").also { it.writeText(body) }
    }

    private fun uids(prefix: String, count: Int) = (0 until count).map { "$prefix-$it" }

    @Suppress("UNCHECKED_CAST")
    private fun uidOf(row: Any?): String = (row as Map<String, Any?>)["sourceUid"] as String

    /** Drains until nothing is left; returns every sourceUid in the order handed out. */
    private fun drainAll(limit: Int, maxChars: Long = PendingStore.DRAIN_CHARS): List<String> {
        val seen = mutableListOf<String>()
        var calls = 0
        do {
            val d = store.drain(limit, maxChars)
            assertTrue("a drain returned more than its limit", d.size <= limit)
            seen += d.samples.map(::uidOf) + d.workouts.map(::uidOf)
            calls += 1
            assertTrue("drain did not finish", calls < 10_000)
        } while (d.moreLeft)
        return seen
    }

    private fun jsonFiles() = dir.listFiles { f -> f.name.endsWith(".json") }!!.toList()

    // ------------------------------------------------------------ budget

    @Test
    fun drain_moreRowsThanLimit_takesOldestFirstAndStopsAtLimit() {
        legacyFile(1000, uids("a", 1000))
        legacyFile(2000, uids("b", 1000))
        legacyFile(3000, uids("c", 1000))

        val first = store.drain(1500)

        assertEquals(1500, first.size)
        assertTrue(first.moreLeft)
        assertEquals(uids("a", 1000) + uids("b", 500), first.samples.map(::uidOf))
        // The oldest file is gone, the second was split, the newest is untouched.
        assertFalse(File(dir, "1000.json").exists())
        assertFalse(File(dir, "2000.json").exists())
        assertTrue(File(dir, "3000.json").exists())

        val second = store.drain(1500)
        assertEquals(uids("b", 1000).drop(500) + uids("c", 1000), second.samples.map(::uidOf))
        assertFalse(second.moreLeft)
        assertTrue(jsonFiles().isEmpty())
    }

    @Test
    fun drain_exactlyOneFileOfRows_deletesItAndReportsTheNextFile() {
        legacyFile(1000, uids("a", 10))
        legacyFile(2000, uids("b", 10))

        val d = store.drain(10)

        assertEquals(uids("a", 10), d.samples.map(::uidOf))
        assertTrue(d.moreLeft)
        assertFalse(File(dir, "1000.json").exists())
        assertTrue(File(dir, "2000.json").exists())
    }

    @Test
    fun drain_emptyFolder_returnsNothing() {
        val d = store.drain(1500)
        assertEquals(0, d.size)
        assertFalse(d.moreLeft)
    }

    @Test
    fun drain_zeroLimit_takesNothingButReportsMore() {
        legacyFile(1000, uids("a", 3))
        val d = store.drain(0)
        assertEquals(0, d.size)
        assertTrue(d.moreLeft)
        assertTrue(File(dir, "1000.json").exists())
    }

    @Test
    fun drain_characterBudget_stopsEarlyButAlwaysTakesOneRow() {
        legacyFile(1000, uids("a", 50))

        val d = store.drain(1500, maxChars = 1)

        assertTrue(d.size >= 1)
        assertTrue(d.size < 50)
        assertTrue(d.moreLeft)
        assertEquals(uids("a", 50), d.samples.map(::uidOf) + drainAll(1500, maxChars = 1))
    }

    // ------------------------------------------------------------ partial files

    @Test
    fun drain_partialFile_keepsUnreadSamplesAndWorkouts() {
        legacyFile(1000, uids("s", 2500), uids("w", 3))

        val first = store.drain(1000)

        assertEquals(uids("s", 1000), first.samples.map(::uidOf))
        assertTrue(first.workouts.isEmpty())
        assertTrue(first.moreLeft)
        assertFalse(File(dir, "1000.json").exists())
        // The rest was rewritten as chunks that keep the original's age.
        val chunks = jsonFiles()
        assertTrue(chunks.isNotEmpty())
        assertTrue(chunks.all { it.name.startsWith("1000-") })
        assertTrue(dir.listFiles { f -> f.name.endsWith(".tmp") }!!.isEmpty())

        val rest = drainAll(1000)
        assertEquals((uids("s", 2500).drop(1000) + uids("w", 3)).toSet(), rest.toSet())
        assertEquals("no row handed out twice", rest.size, rest.toSet().size)
        assertTrue(jsonFiles().isEmpty())
    }

    @Test
    fun drain_partialFile_isSplitIntoBoundedChunksOnce() {
        legacyFile(1000, uids("s", 10_500))

        store.drain(100)

        // 10,400 unread rows in chunks of at most CHUNK_ROWS: eleven files.
        assertEquals(11, jsonFiles().size)
        assertTrue(jsonFiles().all { it.length() < PendingStore.CHUNK_ROWS * 400L })
        // Later drains split the first chunk again; the original order holds.
        assertEquals(uids("s", 10_500).drop(100), drainAll(1300))
    }

    @Test
    fun drain_partialFile_staysBeforeNewerFiles() {
        legacyFile(1000, uids("old", 30))
        legacyFile(5000, uids("new", 5))

        val seen = drainAll(7)

        assertEquals(uids("old", 30) + uids("new", 5), seen)
    }

    @Test
    fun drain_preservesValuesNullsAndNesting() {
        File(dir, "1000.json").writeText(
            """{"samples":[{"sourceUid":"x","value":61.5,"whole":72,"big":12345678901,"end":null,""" +
                """"ok":true,"meta":{"hcType":"StepsRecord","list":[1,"two",null]}}],"workouts":[]}"""
        )

        @Suppress("UNCHECKED_CAST")
        val row = store.drain(10).samples.single() as Map<String, Any?>

        assertEquals(61.5, row["value"])
        assertEquals(72, row["whole"])
        assertEquals(12345678901L, row["big"])
        assertTrue(row.containsKey("end"))
        assertNull(row["end"])
        assertEquals(true, row["ok"])
        assertEquals(mapOf("hcType" to "StepsRecord", "list" to listOf(1, "two", null)), row["meta"])
    }

    @Test
    fun drain_splitRows_roundTripUnchanged() {
        File(dir, "1000.json").writeText(
            """{"samples":[{"sourceUid":"a","value":1.0},{"sourceUid":"b","value":61.5,"end":null,"meta":{"k":[1,2]}}]}"""
        )

        store.drain(1)
        @Suppress("UNCHECKED_CAST")
        val row = store.drain(1).samples.single() as Map<String, Any?>

        assertEquals(mapOf("sourceUid" to "b", "value" to 61.5, "end" to null, "meta" to mapOf("k" to listOf(1, 2))), row)
    }

    // ------------------------------------------------------------ damaged files

    @Test
    fun drain_truncatedFile_keepsRowsBeforeTheDamageAndMovesOn() {
        val whole = """{"samples":[${uids("t", 5).joinToString(",") { sample(it) }}]}"""
        File(dir, "1000.json").writeText(whole.substring(0, whole.length - 40))
        legacyFile(2000, uids("ok", 3))

        val d = store.drain(1500)

        assertEquals(uids("t", 4) + uids("ok", 3), d.samples.map(::uidOf))
        assertFalse(d.moreLeft)
        assertTrue(jsonFiles().isEmpty())
    }

    @Test
    fun drain_notJson_isDroppedAndDoesNotBlockLaterFiles() {
        File(dir, "1000.json").writeText("this is not json")
        File(dir, "1500.json").writeText("")
        legacyFile(2000, uids("ok", 2))

        val d = store.drain(1500)

        assertEquals(uids("ok", 2), d.samples.map(::uidOf))
        assertTrue(jsonFiles().isEmpty())
    }

    @Test
    fun drain_damageAfterTheBudget_keepsTheReadableRest() {
        val whole = """{"samples":[${uids("t", 20).joinToString(",") { sample(it) }}]}"""
        File(dir, "1000.json").writeText(whole.substring(0, whole.length - 40))

        val seen = drainAll(5)

        assertEquals(uids("t", 19), seen)
    }

    @Test
    fun drain_leftoverTempFile_isRemovedAndIgnored() {
        File(dir, "1000-abc-0.json.tmp").writeText("""{"samples":[""")
        legacyFile(2000, uids("ok", 1))

        val d = store.drain(10)

        assertEquals(listOf("ok-0"), d.samples.map(::uidOf))
        assertTrue(dir.listFiles()!!.isEmpty())
    }

    @Test
    fun drain_unknownKeysAndNonArrays_areSkipped() {
        File(dir, "1000.json").writeText(
            """{"version":2,"samples":{"oops":1},"extra":[1,2,3],"workouts":[${workout("w-0")}]}"""
        )

        val d = store.drain(10)

        assertEquals(listOf("w-0"), d.workouts.map(::uidOf))
        assertTrue(d.samples.isEmpty())
    }

    // ------------------------------------------------------------ many files

    @Test
    fun drain_manyFiles_drainsEverythingInOrderWithinTheLimit() {
        val expected = mutableListOf<String>()
        for (i in 0 until 300) {
            val ids = uids("f$i", 10)
            legacyFile(1_000_000L + i, ids)
            expected += ids
        }

        val seen = drainAll(1500)

        assertEquals(expected, seen)
        assertTrue(jsonFiles().isEmpty())
    }

    @Test
    fun drain_largeBacklog_neverHandsOutMoreThanTheCharacterBudget() {
        // About 9 MB in one old-style file, the shape that exhausted the heap.
        legacyFile(1000, uids("big", 40_000))

        val first = store.drain(1_000_000, maxChars = 512 * 1024)

        assertTrue(first.moreLeft)
        assertTrue("took ${first.size} rows", first.size in 1..3000)
        val rest = drainAll(1500)
        assertEquals(40_000, first.size + rest.size)
        assertEquals(40_000, (first.samples.map(::uidOf) + rest).toSet().size)
    }

    // ------------------------------------------------------------ write and growth

    @Test
    fun write_thenDrain_returnsTheSameRows() {
        val samples = listOf(
            mapOf("sourceUid" to "a", "value" to 61.5, "end" to null, "meta" to mapOf("hcType" to "HeartRateRecord")),
            mapOf("sourceUid" to "b", "value" to 72.0, "end" to "2026-11-01T07:00:00Z", "meta" to emptyMap<String, Any?>()),
        )
        val workouts = listOf(mapOf("sourceUid" to "w", "type" to "walking", "distanceM" to null))

        assertTrue(store.write(samples, workouts, now = 1000))
        val d = store.drain(10)

        assertEquals(samples, d.samples)
        assertEquals(workouts, d.workouts)
    }

    @Test
    fun write_nothing_createsNoFile() {
        assertTrue(store.write(emptyList(), emptyList()))
        assertTrue(jsonFiles().isEmpty())
    }

    @Test
    fun write_largeBatch_isChunked() {
        val samples = (0 until 2500).map { mapOf("sourceUid" to "s-$it", "value" to it) }

        assertTrue(store.write(samples, emptyList(), now = 1000))

        assertEquals(3, jsonFiles().size)
        assertEquals(samples.map { it["sourceUid"] }, drainAll(1500))
    }

    @Test
    fun write_keepsOrderAcrossRuns() {
        store.write(listOf(mapOf("sourceUid" to "later")), emptyList(), now = 2000)
        store.write(listOf(mapOf("sourceUid" to "earlier")), emptyList(), now = 1000)

        assertEquals(listOf("earlier", "later"), drainAll(1))
    }

    @Test
    fun write_sameMillisecond_doesNotOverwrite() {
        store.write(listOf(mapOf("sourceUid" to "one")), emptyList(), now = 1000)
        store.write(listOf(mapOf("sourceUid" to "two")), emptyList(), now = 1000)

        assertEquals(listOf("one", "two"), drainAll(10))
    }

    @Test
    fun isFull_byFileCount() {
        for (i in 0 until PendingStore.MAX_FILES - 1) legacyFile(1000L + i, listOf("x$i"))
        assertFalse(store.isFull())
        legacyFile(5000, listOf("last"))
        assertTrue(store.isFull())
        store.drain(1500)
        assertFalse(store.isFull())
    }

    @Test
    fun isFull_bySize() {
        File(dir, "1000.json").writeText("""{"samples":[],"pad":"${"x".repeat(PendingStore.MAX_BYTES.toInt())}"}""")
        assertTrue(store.isFull())
        assertEquals(1, store.stats().files)
    }
}
