package app.selfkept.selfkept.health

import android.app.Activity
import android.os.Bundle
import android.util.TypedValue
import android.widget.ScrollView
import android.widget.TextView

/**
 * Health Connect opens this when the user taps the privacy policy link on its
 * permission screen. Plain text, no product name in code: the app label comes
 * from the manifest, which the brand generator writes.
 */
class HealthPermissionsRationaleActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val appName = applicationInfo.loadLabel(packageManager).toString()
        val text = TextView(this).apply {
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 16f)
            val pad = (24 * resources.displayMetrics.density).toInt()
            setPadding(pad, pad, pad, pad)
            this.text = listOf(
                "$appName reads your health data on this phone and sends it to the server you set up yourself. ",
                "Nothing goes anywhere else: no analytics, no third parties, no cloud of ours.",
                "",
                "It reads steps, distance, energy, floors, exercise sessions, heart rate, resting heart rate, ",
                "heart rate variability, blood oxygen, respiratory rate, skin and body temperature, sleep, VO2 max, ",
                "weight, body fat, height, menstrual flow, basal body temperature, blood glucose and blood pressure.",
                "",
                "Background and history access let the app keep your record up to date without being opened ",
                "and load the last 90 days when you first connect.",
                "",
                "You can turn any of this off in Health Connect at any time. Data already on your server stays there ",
                "until you delete it.",
            ).joinToString("")
        }
        setContentView(ScrollView(this).apply { addView(text) })
    }
}
