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
 * Each run reads the delta with the same [HealthReader] the plugin uses and
 * writes the mapped rows to a file under files/health_pending/; the plugin's
 * next `collectSince` drains those files first. Then:
 *
 * - When the app's engine is alive in this process, a change is posted on
 *   [HealthChangeBus] and the open app syncs at once.
 * - When the app is closed, [BackgroundUpload] starts a headless engine that
 *   sends the files, at most once an hour so the phone is not woken into a
 *   full engine every 15 minutes.
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
            // With the app closed nothing else would send these until it is
            // next opened.
            if (!BackgroundUpload.foregroundAlive &&
                PendingFiles.hasAny(applicationContext) &&
                BackgroundUpload.isDue(applicationContext)
            ) {
                BackgroundUpload.run(applicationContext)
            }
            Result.success()
        } catch (_: Exception) {
            Result.retry()
        }
    }
}
