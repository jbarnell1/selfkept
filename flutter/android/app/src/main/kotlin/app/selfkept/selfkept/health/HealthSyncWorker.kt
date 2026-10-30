package app.selfkept.selfkept.health

import android.content.Context
import android.os.Build
import androidx.health.connect.client.HealthConnectClient
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.NetworkType
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import java.time.Duration
import java.time.Instant
import java.util.concurrent.TimeUnit

/**
 * Periodic background collection, every 15 minutes (the WorkManager minimum)
 * with a network constraint, as the contract asks.
 *
 * Why a file hand-off rather than a headless Flutter engine: the worker has
 * no Dart side. Starting a FlutterEngine from a worker costs a few hundred
 * milliseconds of CPU and tens of megabytes every 15 minutes, needs a
 * registered Dart entry point and callback dispatcher, and makes the sync
 * engine run in two isolates that both own the outbox. Writing the mapped rows
 * to a file and letting the normal app ingest them keeps one owner of the
 * outbox, one sync engine, and a worker that finishes in well under a second.
 * The cost is latency: rows wait until the app is next opened (or resumed by
 * the user); the contract's 15-minute cadence is met for collection, and the
 * send happens on the next foreground. When the app process is alive the
 * worker also posts on [HealthChangeBus], so an open app syncs at once.
 */
class HealthSyncWorker(context: Context, params: WorkerParameters) : CoroutineWorker(context, params) {

    companion object {
        const val UNIQUE_NAME = "app.selfkept.health.sync"
        private const val PAGE = 1500

        /** Idempotent: re-scheduling keeps the existing periodic request. */
        fun schedule(context: Context) {
            val constraints = Constraints.Builder()
                .setRequiredNetworkType(NetworkType.CONNECTED)
                .setRequiresBatteryNotLow(true)
                .build()
            val request = PeriodicWorkRequestBuilder<HealthSyncWorker>(15, TimeUnit.MINUTES)
                .setConstraints(constraints)
                .build()
            WorkManager.getInstance(context).enqueueUniquePeriodicWork(
                UNIQUE_NAME, ExistingPeriodicWorkPolicy.KEEP, request,
            )
        }

        fun cancel(context: Context) {
            WorkManager.getInstance(context).cancelUniqueWork(UNIQUE_NAME)
        }
    }

    override suspend fun doWork(): Result {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return Result.success()
        if (HealthConnectClient.getSdkStatus(applicationContext) != HealthConnectClient.SDK_AVAILABLE) {
            return Result.success()
        }
        val client = HealthConnectClient.getOrCreate(applicationContext)
        val granted = try {
            client.permissionController.getGrantedPermissions()
        } catch (_: Exception) {
            return Result.retry()
        }
        // Without the background permission Health Connect refuses reads from
        // a worker; try again next period rather than fail loudly.
        if (!granted.contains(HealthReader.BACKGROUND)) return Result.success()

        return try {
            val reader = HealthReader(applicationContext)
            // The worker never backfills more than one page; the foreground
            // pass carries the bulk of the first 90 days.
            val page = reader.collect(Instant.now().minus(Duration.ofDays(90)), PAGE)
            PendingFiles.write(applicationContext, page.samples, page.workouts)
            if (page.samples.length() > 0 || page.workouts.length() > 0) {
                HealthChangeBus.notifyChanged()
            }
            Result.success()
        } catch (_: Exception) {
            Result.retry()
        }
    }
}
