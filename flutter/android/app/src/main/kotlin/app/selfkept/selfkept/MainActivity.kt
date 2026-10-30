package app.selfkept.selfkept

import app.selfkept.selfkept.health.HealthConnectCollector
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine

class MainActivity : FlutterActivity() {
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        // The Health Connect collector is part of the app, not a pub package,
        // so it is added next to the generated plugins.
        flutterEngine.plugins.add(HealthConnectCollector())
    }
}
