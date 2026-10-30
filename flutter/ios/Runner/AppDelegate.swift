import Flutter
import UIKit

@main
@objc class AppDelegate: FlutterAppDelegate, FlutterImplicitEngineDelegate {
  override func application(
    _ application: UIApplication,
    didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?
  ) -> Bool {
    return super.application(application, didFinishLaunchingWithOptions: launchOptions)
  }

  func didInitializeImplicitFlutterEngine(_ engineBridge: FlutterImplicitEngineBridge) {
    GeneratedPluginRegistrant.register(with: engineBridge.pluginRegistry)
    // The HealthKit collector is part of the app, not a pub package, so it is
    // registered by hand next to the generated plugins.
    if let registrar = engineBridge.pluginRegistry.registrar(forPlugin: "HealthKitCollector") {
      HealthKitCollector.register(with: registrar)
    }
  }
}
