package app.selfkept.selfkept.health

import android.app.Activity
import android.content.Context
import android.content.Intent
import android.os.Build
import android.os.Handler
import android.os.Looper
import androidx.health.connect.client.HealthConnectClient
import androidx.health.connect.client.PermissionController
import androidx.health.connect.client.permission.HealthPermission
import androidx.health.connect.client.records.ActiveCaloriesBurnedRecord
import androidx.health.connect.client.records.BasalBodyTemperatureRecord
import androidx.health.connect.client.records.BasalMetabolicRateRecord
import androidx.health.connect.client.records.BloodGlucoseRecord
import androidx.health.connect.client.records.BloodPressureRecord
import androidx.health.connect.client.records.BodyFatRecord
import androidx.health.connect.client.records.BodyTemperatureRecord
import androidx.health.connect.client.records.DistanceRecord
import androidx.health.connect.client.records.ExerciseSessionRecord
import androidx.health.connect.client.records.FloorsClimbedRecord
import androidx.health.connect.client.records.HeartRateRecord
import androidx.health.connect.client.records.HeartRateVariabilityRmssdRecord
import androidx.health.connect.client.records.HeightRecord
import androidx.health.connect.client.records.MenstruationFlowRecord
import androidx.health.connect.client.records.OxygenSaturationRecord
import androidx.health.connect.client.records.Record
import androidx.health.connect.client.records.RespiratoryRateRecord
import androidx.health.connect.client.records.RestingHeartRateRecord
import androidx.health.connect.client.records.SkinTemperatureRecord
import androidx.health.connect.client.records.SleepSessionRecord
import androidx.health.connect.client.records.StepsRecord
import androidx.health.connect.client.records.Vo2MaxRecord
import androidx.health.connect.client.records.WeightRecord
import androidx.health.connect.client.records.metadata.Metadata
import androidx.health.connect.client.request.ChangesTokenRequest
import androidx.health.connect.client.request.ReadRecordsRequest
import androidx.health.connect.client.changes.UpsertionChange
import androidx.health.connect.client.time.TimeRangeFilter
import io.flutter.embedding.engine.plugins.FlutterPlugin
import io.flutter.embedding.engine.plugins.activity.ActivityAware
import io.flutter.embedding.engine.plugins.activity.ActivityPluginBinding
import io.flutter.plugin.common.EventChannel
import io.flutter.plugin.common.MethodCall
import io.flutter.plugin.common.MethodChannel
import io.flutter.plugin.common.PluginRegistry
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.time.Duration
import java.time.Instant
import java.time.format.DateTimeFormatter
import kotlin.reflect.KClass

/**
 * The Android half of `HealthCollector` (lib/collectors/health_collector.dart),
 * on top of Health Connect.
 *
 * Channels:
 *   MethodChannel  app.selfkept/health
 *     isAvailable()                                 -> Boolean
 *     deviceInfo()                                  -> { name, platform, osVersion }
 *     requestPermissions({ metrics: [String] })     -> "granted" | "partial" | "denied" | "unavailable"
 *     collectSince({ cursor, backfillFrom, limit }) -> { samples, workouts, cursor, hasMore }
 *   EventChannel   app.selfkept/health/changes
 *     emits once when the periodic worker has written new rows.
 *
 * State: the changes token per record type lives in SharedPreferences on this
 * side (the worker runs without Dart and needs it) and is mirrored into the
 * cursor Dart stores on the server. When Dart passes a cursor and the local
 * store is empty (a reinstall), the cursor seeds the local tokens.
 *
 * Background: see [HealthSyncWorker]. It runs every 15 minutes, reads with the
 * same [HealthReader], writes a JSON file under files/health_pending/, and
 * posts a change on [HealthChangeBus]. The next `collectSince` drains those
 * files first, then reads the live delta.
 */
class HealthConnectCollector : FlutterPlugin, ActivityAware, MethodChannel.MethodCallHandler,
    EventChannel.StreamHandler, PluginRegistry.ActivityResultListener {

    companion object {
        const val METHOD_CHANNEL = "app.selfkept/health"
        const val EVENT_CHANNEL = "app.selfkept/health/changes"
        private const val PERMISSION_REQUEST = 0x4845
    }

    private lateinit var context: Context
    private var activity: Activity? = null
    private var activityBinding: ActivityPluginBinding? = null
    private var methods: MethodChannel? = null
    private var events: EventChannel? = null
    private var sink: EventChannel.EventSink? = null
    private var changePending = false
    private var pendingPermissionResult: MethodChannel.Result? = null
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)
    private val main = Handler(Looper.getMainLooper())

    private val busListener: () -> Unit = {
        main.post {
            val s = sink
            if (s != null) s.success(null) else changePending = true
        }
    }

    // ---------------------------------------------------------------- plugin

    override fun onAttachedToEngine(binding: FlutterPlugin.FlutterPluginBinding) {
        context = binding.applicationContext
        methods = MethodChannel(binding.binaryMessenger, METHOD_CHANNEL).also {
            it.setMethodCallHandler(this)
        }
        events = EventChannel(binding.binaryMessenger, EVENT_CHANNEL).also {
            it.setStreamHandler(this)
        }
        HealthChangeBus.addListener(busListener)
    }

    override fun onDetachedFromEngine(binding: FlutterPlugin.FlutterPluginBinding) {
        HealthChangeBus.removeListener(busListener)
        methods?.setMethodCallHandler(null)
        events?.setStreamHandler(null)
        methods = null
        events = null
        scope.cancel()
    }

    override fun onAttachedToActivity(binding: ActivityPluginBinding) {
        activity = binding.activity
        activityBinding = binding
        binding.addActivityResultListener(this)
    }

    override fun onDetachedFromActivityForConfigChanges() = onDetachedFromActivity()

    override fun onReattachedToActivityForConfigChanges(binding: ActivityPluginBinding) =
        onAttachedToActivity(binding)

    override fun onDetachedFromActivity() {
        activityBinding?.removeActivityResultListener(this)
        activityBinding = null
        activity = null
    }

    // ---------------------------------------------------------------- events

    override fun onListen(arguments: Any?, eventSink: EventChannel.EventSink?) {
        sink = eventSink
        if (changePending) {
            changePending = false
            eventSink?.success(null)
        }
    }

    override fun onCancel(arguments: Any?) {
        sink = null
    }

    // ---------------------------------------------------------------- methods

    override fun onMethodCall(call: MethodCall, result: MethodChannel.Result) {
        when (call.method) {
            "isAvailable" -> result.success(isAvailable())
            "deviceInfo" -> result.success(
                mapOf(
                    "name" to deviceName(),
                    "platform" to "android",
                    "osVersion" to Build.VERSION.RELEASE,
                    "model" to Build.MODEL,
                )
            )
            "requestPermissions" -> requestPermissions(result)
            "collectSince" -> {
                val cursor = call.argument<String>("cursor")
                val backfillFrom = call.argument<String>("backfillFrom")
                val limit = call.argument<Int>("limit") ?: 1500
                collectSince(cursor, backfillFrom, limit, result)
            }
            else -> result.notImplemented()
        }
    }

    private fun isAvailable(): Boolean {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return false
        return HealthConnectClient.getSdkStatus(context) == HealthConnectClient.SDK_AVAILABLE
    }

    private fun deviceName(): String {
        val model = Build.MODEL ?: "Android phone"
        val maker = Build.MANUFACTURER ?: ""
        return if (model.startsWith(maker, ignoreCase = true) || maker.isEmpty()) model
        else "${maker.replaceFirstChar { it.uppercase() }} $model"
    }

    // ---------------------------------------------------------------- permissions

    private fun requestPermissions(result: MethodChannel.Result) {
        if (!isAvailable()) {
            result.success("unavailable")
            return
        }
        val host = activity
        if (host == null) {
            result.error("no_activity", "Permissions need a visible screen.", null)
            return
        }
        if (pendingPermissionResult != null) {
            result.error("busy", "A permission request is already showing.", null)
            return
        }
        scope.launch {
            val client = HealthConnectClient.getOrCreate(context)
            val wanted = HealthReader.permissions()
            val granted = withContext(Dispatchers.IO) {
                client.permissionController.getGrantedPermissions()
            }
            if (granted.containsAll(wanted)) {
                HealthSyncWorker.schedule(context)
                result.success("granted")
                return@launch
            }
            pendingPermissionResult = result
            val contract = PermissionController.createRequestPermissionResultContract()
            val intent: Intent = contract.createIntent(host, wanted)
            host.startActivityForResult(intent, PERMISSION_REQUEST)
        }
    }

    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?): Boolean {
        if (requestCode != PERMISSION_REQUEST) return false
        val pending = pendingPermissionResult ?: return true
        pendingPermissionResult = null
        scope.launch {
            val client = HealthConnectClient.getOrCreate(context)
            val granted = withContext(Dispatchers.IO) {
                client.permissionController.getGrantedPermissions()
            }
            val wanted = HealthReader.permissions()
            val readable = wanted.filter { it in granted && it != HealthReader.BACKGROUND && it != HealthReader.HISTORY }
            val state = when {
                granted.containsAll(wanted) -> "granted"
                readable.isNotEmpty() -> "partial"
                else -> "denied"
            }
            if (state != "denied") HealthSyncWorker.schedule(context)
            pending.success(state)
        }
        return true
    }

    // ---------------------------------------------------------------- collect

    private fun collectSince(cursor: String?, backfillFrom: String?, limit: Int, result: MethodChannel.Result) {
        if (!isAvailable()) {
            result.error("unavailable", "Health Connect is not available.", null)
            return
        }
        scope.launch {
            try {
                val payload = withContext(Dispatchers.IO) {
                    val reader = HealthReader(context)
                    reader.seedTokensFromCursor(cursor)
                    val from = backfillFrom?.let { HealthReader.parseInstant(it) }
                        ?: Instant.now().minus(Duration.ofDays(90))
                    // Rows the worker left behind go first; they are already mapped.
                    val staged = PendingFiles.drain(context)
                    val remaining = (limit - staged.samples.length()).coerceAtLeast(0)
                    val live = reader.collect(from, remaining)
                    val samples = JSONArray()
                    for (i in 0 until staged.samples.length()) samples.put(staged.samples.get(i))
                    for (i in 0 until live.samples.length()) samples.put(live.samples.get(i))
                    val workouts = JSONArray()
                    for (i in 0 until staged.workouts.length()) workouts.put(staged.workouts.get(i))
                    for (i in 0 until live.workouts.length()) workouts.put(live.workouts.get(i))
                    JSONObject()
                        .put("samples", samples)
                        .put("workouts", workouts)
                        .put("cursor", reader.cursor())
                        .put("hasMore", live.hasMore)
                }
                result.success(JsonBridge.toFlutter(payload))
            } catch (e: Exception) {
                result.error("collect_failed", e.message ?: e.javaClass.simpleName, null)
            }
        }
    }
}

/**
 * Lets the worker, which has no Flutter engine of its own, poke the plugin
 * when it is alive in the same process. Listeners are invoked on the calling
 * thread; the plugin hops to main.
 */
object HealthChangeBus {
    private val listeners = mutableSetOf<() -> Unit>()

    @Synchronized
    fun addListener(l: () -> Unit) { listeners.add(l) }

    @Synchronized
    fun removeListener(l: () -> Unit) { listeners.remove(l) }

    @Synchronized
    fun notifyChanged() { listeners.toList().forEach { it() } }
}

/**
 * Rows the worker collected while Dart was not running. One file per worker
 * run under files/health_pending/, each a JSON object { samples, workouts }.
 * Files are deleted as they are drained; Dart writes the rows to its outbox
 * before anything is sent, so a crash between the two costs one re-read of
 * the same records, which the server deduplicates on sourceUid.
 */
object PendingFiles {
    private const val DIR = "health_pending"

    class Drained(val samples: JSONArray, val workouts: JSONArray)

    fun dir(context: Context): File = File(context.filesDir, DIR).also { it.mkdirs() }

    fun write(context: Context, samples: JSONArray, workouts: JSONArray) {
        if (samples.length() == 0 && workouts.length() == 0) return
        val file = File(dir(context), "${System.currentTimeMillis()}.json")
        val body = JSONObject().put("samples", samples).put("workouts", workouts)
        File(file.path + ".tmp").apply {
            writeText(body.toString())
            renameTo(file)
        }
    }

    @Synchronized
    fun drain(context: Context): Drained {
        val samples = JSONArray()
        val workouts = JSONArray()
        val files = dir(context).listFiles { f -> f.name.endsWith(".json") }?.sortedBy { it.name } ?: emptyList()
        for (file in files) {
            try {
                val body = JSONObject(file.readText())
                val s = body.optJSONArray("samples") ?: JSONArray()
                val w = body.optJSONArray("workouts") ?: JSONArray()
                for (i in 0 until s.length()) samples.put(s.get(i))
                for (i in 0 until w.length()) workouts.put(w.get(i))
            } catch (_: Exception) {
                // A damaged file is dropped rather than blocking every sync.
            }
            file.delete()
        }
        return Drained(samples, workouts)
    }
}

/** org.json to the plain maps and lists Flutter's StandardMessageCodec accepts. */
object JsonBridge {
    fun toFlutter(value: Any?): Any? = when (value) {
        null, JSONObject.NULL -> null
        is JSONObject -> value.keys().asSequence().associateWith { toFlutter(value.opt(it)) }
        is JSONArray -> (0 until value.length()).map { toFlutter(value.opt(it)) }
        else -> value
    }
}

/**
 * Reads Health Connect and maps records to the ingest contract. Shared by the
 * plugin (foreground) and [HealthSyncWorker] (background).
 *
 * Per record type the local state is either a changes token (steady state) or
 * a backfill page token (first 90 days, paged). The token store is
 * SharedPreferences "health_connect_tokens"; the cursor Dart sees is the same
 * map as JSON: { "StepsRecord": { "token": "...", "page": "...", "done": true } }.
 */
class HealthReader(private val context: Context) {

    class Page(val samples: JSONArray, val workouts: JSONArray, val hasMore: Boolean)

    private class Mapping(
        val name: String,
        val type: KClass<out Record>,
    )

    companion object {
        val BACKGROUND: String = HealthPermission.PERMISSION_READ_HEALTH_DATA_IN_BACKGROUND
        val HISTORY: String = HealthPermission.PERMISSION_READ_HEALTH_DATA_HISTORY
        private const val PREFS = "health_connect_tokens"
        private const val KEY_STATE = "state"
        private const val BACKFILL_PAGE = 1000

        private val mappings: List<Mapping> = listOf(
            Mapping("StepsRecord", StepsRecord::class),
            Mapping("DistanceRecord", DistanceRecord::class),
            Mapping("ActiveCaloriesBurnedRecord", ActiveCaloriesBurnedRecord::class),
            Mapping("BasalMetabolicRateRecord", BasalMetabolicRateRecord::class),
            Mapping("FloorsClimbedRecord", FloorsClimbedRecord::class),
            Mapping("HeartRateRecord", HeartRateRecord::class),
            Mapping("RestingHeartRateRecord", RestingHeartRateRecord::class),
            Mapping("HeartRateVariabilityRmssdRecord", HeartRateVariabilityRmssdRecord::class),
            Mapping("OxygenSaturationRecord", OxygenSaturationRecord::class),
            Mapping("RespiratoryRateRecord", RespiratoryRateRecord::class),
            Mapping("SkinTemperatureRecord", SkinTemperatureRecord::class),
            Mapping("BodyTemperatureRecord", BodyTemperatureRecord::class),
            Mapping("SleepSessionRecord", SleepSessionRecord::class),
            Mapping("Vo2MaxRecord", Vo2MaxRecord::class),
            Mapping("WeightRecord", WeightRecord::class),
            Mapping("BodyFatRecord", BodyFatRecord::class),
            Mapping("HeightRecord", HeightRecord::class),
            Mapping("MenstruationFlowRecord", MenstruationFlowRecord::class),
            Mapping("BasalBodyTemperatureRecord", BasalBodyTemperatureRecord::class),
            Mapping("BloodGlucoseRecord", BloodGlucoseRecord::class),
            Mapping("BloodPressureRecord", BloodPressureRecord::class),
            Mapping("ExerciseSessionRecord", ExerciseSessionRecord::class),
        )

        /** Every read permission the contract needs, plus background and history. */
        fun permissions(): Set<String> {
            val set = mutableSetOf<String>()
            for (m in mappings) set.add(HealthPermission.getReadPermission(m.type))
            set.add(BACKGROUND)
            set.add(HISTORY)
            return set
        }

        private val iso: DateTimeFormatter = DateTimeFormatter.ISO_INSTANT

        fun iso(instant: Instant): String = iso.format(instant)

        fun parseInstant(text: String): Instant = Instant.parse(text)
    }

    private val prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    /** { typeName: { token?, page?, done? } } */
    private val state: JSONObject = try {
        JSONObject(prefs.getString(KEY_STATE, null) ?: "{}")
    } catch (_: Exception) {
        JSONObject()
    }

    private fun save() {
        prefs.edit().putString(KEY_STATE, state.toString()).apply()
    }

    /** A reinstall: Dart hands back the cursor the server kept and we adopt it if we have nothing. */
    fun seedTokensFromCursor(cursor: String?) {
        if (cursor.isNullOrBlank() || state.length() > 0) return
        try {
            val parsed = JSONObject(cursor)
            for (key in parsed.keys()) state.put(key, parsed.getJSONObject(key))
            save()
        } catch (_: Exception) {
            // Not ours (or damaged): start fresh.
        }
    }

    fun cursor(): String = state.toString()

    private fun entry(name: String): JSONObject =
        state.optJSONObject(name) ?: JSONObject().also { state.put(name, it) }

    /**
     * Reads at most [limit] rows across all types. Each type is either in
     * backfill (reads one page from [backfillFrom]) or steady state (reads the
     * changes since its token). The first visit to a type gets a changes token
     * before its backfill starts, so nothing written during the backfill is lost.
     */
    suspend fun collect(backfillFrom: Instant, limit: Int): Page {
        val client = HealthConnectClient.getOrCreate(context)
        val samples = JSONArray()
        val workouts = JSONArray()
        var hasMore = false
        var budget = limit

        for (m in mappings) {
            if (budget <= 0) {
                hasMore = true
                break
            }
            val e = entry(m.name)
            if (!e.has("token")) {
                e.put("token", client.getChangesToken(ChangesTokenRequest(setOf(m.type))))
                e.put("done", false)
            }
            val records = mutableListOf<Record>()

            if (!e.optBoolean("done", false)) {
                // Backfill page. The cast only widens the KClass projection so
                // the request's type parameter can be inferred.
                @Suppress("UNCHECKED_CAST")
                val recordType = m.type as KClass<Record>
                val pageToken: String? = e.optString("page", "").takeIf { it.isNotEmpty() }
                val response = client.readRecords(
                    ReadRecordsRequest(
                        recordType = recordType,
                        timeRangeFilter = TimeRangeFilter.after(backfillFrom),
                        pageSize = minOf(BACKFILL_PAGE, budget),
                        pageToken = pageToken,
                    )
                )
                records.addAll(response.records)
                val next = response.pageToken
                if (next.isNullOrEmpty()) {
                    e.remove("page")
                    e.put("done", true)
                } else {
                    e.put("page", next)
                    hasMore = true
                }
            } else {
                // Steady state: changes since the token.
                var token = e.getString("token")
                var pages = 0
                do {
                    val changes = try {
                        client.getChanges(token)
                    } catch (_: Exception) {
                        // Token expired (about 30 days unused): restart the type.
                        e.remove("token")
                        e.remove("page")
                        e.put("done", false)
                        hasMore = true
                        null
                    } ?: break
                    if (changes.changesTokenExpired) {
                        e.remove("token")
                        e.remove("page")
                        e.put("done", false)
                        hasMore = true
                        break
                    }
                    for (c in changes.changes) {
                        if (c is UpsertionChange) records.add(c.record)
                    }
                    token = changes.nextChangesToken
                    e.put("token", token)
                    pages += 1
                } while (changes.hasMore && records.size < budget && pages < 20)
                if (records.size >= budget) hasMore = true
            }

            for (r in records) RecordMapper.map(r, samples, workouts)
            budget -= records.size
        }
        save()
        return Page(samples, workouts, hasMore)
    }
}

/** Record to contract rows. Units follow the catalogue's stored units. */
object RecordMapper {

    private fun uid(meta: Metadata, suffix: String = ""): String = "hc-" + meta.id + suffix

    private fun base(
        uid: String, metric: String, start: Instant, end: Instant?, value: Double, unit: String, meta: JSONObject,
    ): JSONObject = JSONObject()
        .put("sourceUid", uid)
        .put("metric", metric)
        .put("start", HealthReader.iso(start))
        .put("end", if (end == null) JSONObject.NULL else HealthReader.iso(end))
        .put("value", value)
        .put("unit", unit)
        .put("meta", meta)

    private fun meta(record: Record, type: String): JSONObject {
        val m = JSONObject().put("hcType", type)
        val origin = record.metadata.dataOrigin.packageName
        if (origin.isNotEmpty()) m.put("sourcePackage", origin)
        record.metadata.device?.model?.let { m.put("device", it) }
        return m
    }

    fun map(r: Record, samples: JSONArray, workouts: JSONArray) {
        when (r) {
            is StepsRecord -> samples.put(
                base(uid(r.metadata), "steps", r.startTime, r.endTime, r.count.toDouble(), "count", meta(r, "StepsRecord"))
            )
            is DistanceRecord -> samples.put(
                base(uid(r.metadata), "distance_m", r.startTime, r.endTime, r.distance.inMeters, "m", meta(r, "DistanceRecord"))
            )
            is ActiveCaloriesBurnedRecord -> samples.put(
                base(uid(r.metadata), "active_energy_kcal", r.startTime, r.endTime, r.energy.inKilocalories, "kcal", meta(r, "ActiveCaloriesBurnedRecord"))
            )
            is BasalMetabolicRateRecord -> {
                // A rate (kcal/day) sampled at a moment; stored as the daily figure.
                val m = meta(r, "BasalMetabolicRateRecord").put("kind", "rate_per_day")
                samples.put(base(uid(r.metadata), "basal_energy_kcal", r.time, r.time.plus(Duration.ofDays(1)), r.basalMetabolicRate.inKilocaloriesPerDay, "kcal", m))
            }
            is FloorsClimbedRecord -> samples.put(
                base(uid(r.metadata), "flights", r.startTime, r.endTime, r.floors, "count", meta(r, "FloorsClimbedRecord"))
            )
            is HeartRateRecord -> {
                // One sample per reading inside the record.
                val m = meta(r, "HeartRateRecord")
                r.samples.forEachIndexed { i, s ->
                    samples.put(base(uid(r.metadata, "-$i"), "heart_rate_bpm", s.time, null, s.beatsPerMinute.toDouble(), "bpm", m))
                }
            }
            is RestingHeartRateRecord -> samples.put(
                base(uid(r.metadata), "resting_hr_bpm", r.time, null, r.beatsPerMinute.toDouble(), "bpm", meta(r, "RestingHeartRateRecord"))
            )
            is HeartRateVariabilityRmssdRecord -> samples.put(
                base(uid(r.metadata), "hrv_rmssd_ms", r.time, null, r.heartRateVariabilityMillis, "ms", meta(r, "HeartRateVariabilityRmssdRecord"))
            )
            is OxygenSaturationRecord -> samples.put(
                base(uid(r.metadata), "spo2_pct", r.time, null, r.percentage.value, "%", meta(r, "OxygenSaturationRecord"))
            )
            is RespiratoryRateRecord -> samples.put(
                base(uid(r.metadata), "respiratory_rate_bpm", r.time, null, r.rate, "breaths/min", meta(r, "RespiratoryRateRecord"))
            )
            is SkinTemperatureRecord -> {
                // Deltas against the record's baseline, one per reading.
                val m = meta(r, "SkinTemperatureRecord").put("kind", "delta")
                r.deltas.forEachIndexed { i, d ->
                    samples.put(base(uid(r.metadata, "-$i"), "wrist_temp_delta_c", d.time, null, d.delta.inCelsius, "C", m))
                }
            }
            is BodyTemperatureRecord -> samples.put(
                base(uid(r.metadata), "body_temp_c", r.time, null, r.temperature.inCelsius, "C", meta(r, "BodyTemperatureRecord"))
            )
            is SleepSessionRecord -> mapSleep(r, samples)
            is Vo2MaxRecord -> samples.put(
                base(uid(r.metadata), "vo2max", r.time, null, r.vo2MillilitersPerMinuteKilogram, "mL/kg/min", meta(r, "Vo2MaxRecord"))
            )
            is WeightRecord -> samples.put(
                base(uid(r.metadata), "weight_kg", r.time, null, r.weight.inKilograms, "kg", meta(r, "WeightRecord"))
            )
            is BodyFatRecord -> samples.put(
                base(uid(r.metadata), "body_fat_pct", r.time, null, r.percentage.value, "%", meta(r, "BodyFatRecord"))
            )
            is HeightRecord -> samples.put(
                base(uid(r.metadata), "height_cm", r.time, null, r.height.inMeters * 100.0, "cm", meta(r, "HeightRecord"))
            )
            is MenstruationFlowRecord -> {
                val (value, label) = when (r.flow) {
                    MenstruationFlowRecord.FLOW_LIGHT -> 1 to "light"
                    MenstruationFlowRecord.FLOW_MEDIUM -> 2 to "medium"
                    MenstruationFlowRecord.FLOW_HEAVY -> 3 to "heavy"
                    else -> 4 to "unspecified"
                }
                val m = meta(r, "MenstruationFlowRecord").put("flow", label)
                samples.put(base(uid(r.metadata), "menstrual_flow", r.time, r.time.plus(Duration.ofDays(1)), value.toDouble(), "level", m))
            }
            is BasalBodyTemperatureRecord -> samples.put(
                base(uid(r.metadata), "basal_body_temp_c", r.time, null, r.temperature.inCelsius, "C", meta(r, "BasalBodyTemperatureRecord"))
            )
            is BloodGlucoseRecord -> samples.put(
                base(uid(r.metadata), "blood_glucose_mmol", r.time, null, r.level.inMillimolesPerLiter, "mmol/L", meta(r, "BloodGlucoseRecord"))
            )
            is BloodPressureRecord -> {
                val m = meta(r, "BloodPressureRecord")
                samples.put(base(uid(r.metadata, "-sys"), "blood_pressure_sys", r.time, null, r.systolic.inMillimetersOfMercury, "mmHg", m))
                samples.put(base(uid(r.metadata, "-dia"), "blood_pressure_dia", r.time, null, r.diastolic.inMillimetersOfMercury, "mmHg", m))
            }
            is ExerciseSessionRecord -> mapExercise(r, samples, workouts)
            else -> Unit
        }
    }

    /** Contract scheme: 0 in_bed, 1 awake, 2 light, 3 deep, 4 rem, 5 unspecified. */
    private fun mapSleep(r: SleepSessionRecord, samples: JSONArray) {
        val m = meta(r, "SleepSessionRecord")
        if (r.stages.isEmpty()) {
            samples.put(base(uid(r.metadata), "sleep_stage", r.startTime, r.endTime, 5.0, "stage", JSONObject(m.toString()).put("stage", "unspecified")))
            return
        }
        r.stages.forEachIndexed { i, s ->
            val (value, label) = when (s.stage) {
                SleepSessionRecord.STAGE_TYPE_AWAKE,
                SleepSessionRecord.STAGE_TYPE_AWAKE_IN_BED,
                SleepSessionRecord.STAGE_TYPE_OUT_OF_BED -> 1 to "awake"
                SleepSessionRecord.STAGE_TYPE_LIGHT -> 2 to "light"
                SleepSessionRecord.STAGE_TYPE_DEEP -> 3 to "deep"
                SleepSessionRecord.STAGE_TYPE_REM -> 4 to "rem"
                SleepSessionRecord.STAGE_TYPE_SLEEPING -> 5 to "unspecified"
                else -> 5 to "unspecified"
            }
            samples.put(base(uid(r.metadata, "-$i"), "sleep_stage", s.startTime, s.endTime, value.toDouble(), "stage", JSONObject(m.toString()).put("stage", label)))
        }
    }

    /** A workout row, plus an exercise_min sample derived from the session length. */
    private fun mapExercise(r: ExerciseSessionRecord, samples: JSONArray, workouts: JSONArray) {
        val minutes = Duration.between(r.startTime, r.endTime).toMillis() / 60000.0
        val m = meta(r, "ExerciseSessionRecord").put("exerciseType", r.exerciseType)
        samples.put(base(uid(r.metadata, "-min"), "exercise_min", r.startTime, r.endTime, minutes, "min", m))
        val w = JSONObject()
            .put("sourceUid", uid(r.metadata))
            .put("type", exerciseName(r.exerciseType))
            .put("start", HealthReader.iso(r.startTime))
            .put("end", HealthReader.iso(r.endTime))
            .put("distanceM", JSONObject.NULL)
            .put("energyKcal", JSONObject.NULL)
            .put("avgHr", JSONObject.NULL)
            .put("maxHr", JSONObject.NULL)
            .put("routeGeoJson", JSONObject.NULL)
            .put("meta", JSONObject(m.toString()).put("title", r.title ?: JSONObject.NULL))
        // Distance, energy and heart rate during a session are separate
        // records in Health Connect; they arrive as their own samples and the
        // server can attribute them to the session by time.
        workouts.put(w)
    }

    private fun exerciseName(type: Int): String = when (type) {
        ExerciseSessionRecord.EXERCISE_TYPE_RUNNING, ExerciseSessionRecord.EXERCISE_TYPE_RUNNING_TREADMILL -> "running"
        ExerciseSessionRecord.EXERCISE_TYPE_WALKING -> "walking"
        ExerciseSessionRecord.EXERCISE_TYPE_BIKING, ExerciseSessionRecord.EXERCISE_TYPE_BIKING_STATIONARY -> "cycling"
        ExerciseSessionRecord.EXERCISE_TYPE_SWIMMING_POOL, ExerciseSessionRecord.EXERCISE_TYPE_SWIMMING_OPEN_WATER -> "swimming"
        ExerciseSessionRecord.EXERCISE_TYPE_HIKING -> "hiking"
        ExerciseSessionRecord.EXERCISE_TYPE_YOGA -> "yoga"
        ExerciseSessionRecord.EXERCISE_TYPE_ROWING, ExerciseSessionRecord.EXERCISE_TYPE_ROWING_MACHINE -> "rowing"
        ExerciseSessionRecord.EXERCISE_TYPE_ELLIPTICAL -> "elliptical"
        ExerciseSessionRecord.EXERCISE_TYPE_STAIR_CLIMBING, ExerciseSessionRecord.EXERCISE_TYPE_STAIR_CLIMBING_MACHINE -> "stair_climbing"
        ExerciseSessionRecord.EXERCISE_TYPE_STRENGTH_TRAINING, ExerciseSessionRecord.EXERCISE_TYPE_WEIGHTLIFTING -> "strength_training"
        ExerciseSessionRecord.EXERCISE_TYPE_HIGH_INTENSITY_INTERVAL_TRAINING -> "hiit"
        ExerciseSessionRecord.EXERCISE_TYPE_PILATES -> "pilates"
        ExerciseSessionRecord.EXERCISE_TYPE_DANCING -> "dance"
        ExerciseSessionRecord.EXERCISE_TYPE_TENNIS -> "tennis"
        ExerciseSessionRecord.EXERCISE_TYPE_FOOTBALL_AUSTRALIAN, ExerciseSessionRecord.EXERCISE_TYPE_SOCCER -> "football"
        ExerciseSessionRecord.EXERCISE_TYPE_BASKETBALL -> "basketball"
        ExerciseSessionRecord.EXERCISE_TYPE_GOLF -> "golf"
        ExerciseSessionRecord.EXERCISE_TYPE_CALISTHENICS -> "core_training"
        ExerciseSessionRecord.EXERCISE_TYPE_STRETCHING -> "stretching"
        ExerciseSessionRecord.EXERCISE_TYPE_GUIDED_BREATHING -> "mind_and_body"
        else -> "other"
    }
}
