package app.selfkept.selfkept.health

import android.content.Context
import io.flutter.FlutterInjector
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.embedding.engine.dart.DartExecutor
import io.flutter.plugin.common.MethodChannel
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

/**
 * Sends what [HealthSyncWorker] collected while the app is closed, so the
 * server is current without anyone opening the app.
 *
 * The worker starts a headless Flutter engine at the Dart entry point
 * `backgroundUpload` in main.dart, which runs the app's own collector, outbox
 * and sync engine once and calls `done` on [CHANNEL]. The engine is then
 * destroyed. One sync engine owns the outbox at a time: the background engine
 * starts only while no activity engine exists, and an activity that starts
 * while it runs stops it ([foregroundStarted]). A stop mid-send costs nothing;
 * ingest is idempotent on sourceUid and unsent rows stay in the outbox.
 *
 * An engine start costs a second or so of CPU and tens of megabytes, so it
 * runs at most once per [INTERVAL_MS], and only when the worker has rows
 * waiting. That keeps the server within the hour of the watch, well before
 * the nightly interpretation at 03:30.
 */
object BackgroundUpload {
    const val CHANNEL = "app.selfkept/background"
    private const val ENTRYPOINT = "backgroundUpload"
    private const val PREFS = "app.selfkept.background"
    private const val LAST_RUN = "lastRunAt"

    const val INTERVAL_MS = 60L * 60 * 1000

    /** WorkManager stops a worker at ten minutes; give up before that. */
    private const val TIMEOUT_MS = 8L * 60 * 1000

    /** True while the activity's engine exists. Written on the main thread. */
    @Volatile
    var foregroundAlive = false
        private set

    // Main thread only.
    private var engine: FlutterEngine? = null
    private var finished: CompletableDeferred<Boolean>? = null

    fun isDue(context: Context): Boolean {
        val last = prefs(context).getLong(LAST_RUN, 0L)
        return System.currentTimeMillis() - last >= INTERVAL_MS
    }

    /** Main thread: the activity's engine is up, so it owns the outbox now. */
    fun foregroundStarted() {
        foregroundAlive = true
        stop()
    }

    /** Main thread: the activity's engine is going away. */
    fun foregroundStopped() {
        foregroundAlive = false
    }

    /** Runs one headless send. True when Dart reported everything sent. */
    suspend fun run(context: Context): Boolean {
        if (foregroundAlive) return false
        val app = context.applicationContext
        prefs(app).edit().putLong(LAST_RUN, System.currentTimeMillis()).apply()
        val done = CompletableDeferred<Boolean>()
        val started = withContext(Dispatchers.Main) {
            if (foregroundAlive || engine != null) return@withContext false
            val flutter = FlutterEngine(app)
            flutter.plugins.add(HealthConnectCollector())
            MethodChannel(flutter.dartExecutor.binaryMessenger, CHANNEL).setMethodCallHandler { call, result ->
                if (call.method == "done") {
                    result.success(null)
                    done.complete(call.arguments as? Boolean ?: false)
                } else {
                    result.notImplemented()
                }
            }
            engine = flutter
            finished = done
            val bundle = FlutterInjector.instance().flutterLoader().findAppBundlePath()
            flutter.dartExecutor.executeDartEntrypoint(DartExecutor.DartEntrypoint(bundle, ENTRYPOINT))
            true
        }
        if (!started) return false
        return try {
            withTimeoutOrNull(TIMEOUT_MS) { done.await() } ?: false
        } finally {
            withContext(NonCancellable + Dispatchers.Main) {
                if (finished === done) stop()
            }
        }
    }

    /** Main thread. Destroys the background engine, if one runs. */
    private fun stop() {
        engine?.destroy()
        engine = null
        finished?.complete(false)
        finished = null
    }

    private fun prefs(context: Context) = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
}
