import Flutter
import HealthKit
import UIKit

// HealthKitCollector
//
// The iOS half of `HealthCollector` (lib/collectors/health_collector.dart).
// Dart owns the state: it passes the cursor in and stores the cursor that comes
// back. This file only reads HealthKit and maps samples to the ingest contract
// in docs/ingest-contract.md.
//
// Channels:
//   MethodChannel  app.selfkept/health
//     isAvailable()                                   -> Bool
//     deviceInfo()                                    -> { name, platform, osVersion }
//     requestPermissions({ metrics: [String] })       -> "granted" | "denied" | "unavailable"
//     collectSince({ cursor, backfillFrom, limit })   -> { samples, workouts, cursor, hasMore }
//   EventChannel   app.selfkept/health/changes
//     emits once whenever an HKObserverQuery fires or a background fetch lands.
//
// Cursor format: a JSON object mapping an HKSampleType identifier to the
// base64 of its archived HKQueryAnchor, for example
//   {"HKQuantityTypeIdentifierHeartRate":"YnBsaXN0MDD...","HKWorkoutTypeIdentifier":"..."}
// A type with no entry has never been read: the first query is bounded by
// `backfillFrom` (90 days by default) and paged by `limit`.
//
// This file was written on a machine without Xcode and has not been compiled.
// It targets the iOS 13 deployment target of the project and guards every
// iOS 16 API with #available.

final class HealthKitCollector: NSObject, FlutterPlugin, FlutterStreamHandler {

  static let methodChannelName = "app.selfkept/health"
  static let eventChannelName = "app.selfkept/health/changes"

  /// Set once observers have been enabled, so a background launch can
  /// re-register them before Dart asks for anything.
  private static let observersEnabledKey = "app.selfkept.health.observersEnabled"

  private let store = HKHealthStore()
  private let work = DispatchQueue(label: "app.selfkept.health", qos: .utility)

  private var eventSink: FlutterEventSink?
  /// A change that arrived before Dart subscribed; delivered on the next onListen.
  private var changePending = false
  private var observerQueries: [HKObserverQuery] = []

  // MARK: - Registration

  static func register(with registrar: FlutterPluginRegistrar) {
    let instance = HealthKitCollector()
    let methods = FlutterMethodChannel(
      name: methodChannelName, binaryMessenger: registrar.messenger())
    registrar.addMethodCallDelegate(instance, channel: methods)
    let events = FlutterEventChannel(
      name: eventChannelName, binaryMessenger: registrar.messenger())
    events.setStreamHandler(instance)
    registrar.addApplicationDelegate(instance)

    // A background launch triggered by HealthKit must re-create its observer
    // queries at once, or the delivery is lost.
    if HKHealthStore.isHealthDataAvailable()
      && UserDefaults.standard.bool(forKey: observersEnabledKey)
    {
      instance.startObservers()
    }
  }

  // MARK: - Method channel

  func handle(_ call: FlutterMethodCall, result: @escaping FlutterResult) {
    switch call.method {
    case "isAvailable":
      result(HKHealthStore.isHealthDataAvailable())

    case "deviceInfo":
      let device = UIDevice.current
      result([
        "name": device.name,
        "platform": "ios",
        "osVersion": device.systemVersion,
        "model": device.model,
      ])

    case "requestPermissions":
      requestPermissions(result: result)

    case "collectSince":
      let args = call.arguments as? [String: Any] ?? [:]
      let cursor = args["cursor"] as? String
      let backfillFrom = (args["backfillFrom"] as? String).flatMap(Self.parseIso)
      let limit = args["limit"] as? Int ?? 1500
      collectSince(cursor: cursor, backfillFrom: backfillFrom, limit: limit, result: result)

    default:
      result(FlutterMethodNotImplemented)
    }
  }

  // MARK: - Event channel

  func onListen(withArguments arguments: Any?, eventSink events: @escaping FlutterEventSink) -> FlutterError? {
    eventSink = events
    if changePending {
      changePending = false
      events(nil)
    }
    return nil
  }

  func onCancel(withArguments arguments: Any?) -> FlutterError? {
    eventSink = nil
    return nil
  }

  private func emitChange() {
    DispatchQueue.main.async {
      if let sink = self.eventSink {
        sink(nil)
      } else {
        self.changePending = true
      }
    }
  }

  // MARK: - Background fetch (UIBackgroundModes: fetch)

  func application(
    _ application: UIApplication,
    performFetchWithCompletionHandler completionHandler: @escaping (UIBackgroundFetchResult) -> Void
  ) -> Bool {
    emitChange()
    completionHandler(.newData)
    return true
  }

  // MARK: - Permissions

  private func requestPermissions(result: @escaping FlutterResult) {
    guard HKHealthStore.isHealthDataAvailable() else {
      result("unavailable")
      return
    }
    var types = Set<HKObjectType>()
    for mapping in Self.quantityMappings {
      types.insert(mapping.type)
    }
    for mapping in Self.categoryMappings {
      types.insert(mapping.type)
    }
    types.insert(HKObjectType.workoutType())

    // Read-only. HealthKit does not reveal whether read access was granted
    // (a denied type just returns no samples), so a completed prompt counts
    // as granted; Dart treats "partial" and "granted" alike.
    store.requestAuthorization(toShare: nil, read: types) { ok, error in
      DispatchQueue.main.async {
        if ok {
          self.startObservers()
          result("granted")
        } else if let error = error {
          result(FlutterError(code: "permission", message: error.localizedDescription, details: nil))
        } else {
          result("denied")
        }
      }
    }
  }

  // MARK: - Observers and background delivery

  private func startObservers() {
    guard observerQueries.isEmpty else { return }
    var types: [HKSampleType] = Self.quantityMappings.map { $0.type as HKSampleType }
    types.append(contentsOf: Self.categoryMappings.map { $0.type as HKSampleType })
    types.append(HKObjectType.workoutType())

    for type in types {
      let query = HKObserverQuery(sampleType: type, predicate: nil) { [weak self] _, completion, _ in
        // Tell Dart, then acknowledge. Dart runs collectSince in response.
        self?.emitChange()
        completion()
      }
      store.execute(query)
      observerQueries.append(query)
      store.enableBackgroundDelivery(for: type, frequency: .hourly) { _, _ in }
    }
    UserDefaults.standard.set(true, forKey: Self.observersEnabledKey)
  }

  // MARK: - Collect

  private func collectSince(
    cursor: String?, backfillFrom: Date?, limit: Int, result: @escaping FlutterResult
  ) {
    guard HKHealthStore.isHealthDataAvailable() else {
      result(FlutterError(code: "unavailable", message: "HealthKit is not available.", details: nil))
      return
    }
    var anchors = Self.decodeCursor(cursor)
    let backfillStart = backfillFrom ?? Calendar.current.date(byAdding: .day, value: -90, to: Date())!

    work.async {
      var samples: [[String: Any]] = []
      var workouts: [[String: Any]] = []
      var hasMore = false
      var budget = max(limit, 1)

      // Quantity types.
      for mapping in Self.quantityMappings where budget > 0 {
        let page = self.runAnchored(
          type: mapping.type, key: mapping.type.identifier, anchors: &anchors,
          backfillStart: backfillStart, limit: budget)
        for case let q as HKQuantitySample in page.samples {
          samples.append(Self.encodeQuantity(q, mapping: mapping))
        }
        budget -= page.samples.count
        if page.hitLimit { hasMore = true }
      }

      // Category types: sleep stages and menstrual flow.
      for mapping in Self.categoryMappings where budget > 0 {
        let page = self.runAnchored(
          type: mapping.type, key: mapping.type.identifier, anchors: &anchors,
          backfillStart: backfillStart, limit: budget)
        for case let c as HKCategorySample in page.samples {
          if let encoded = Self.encodeCategory(c, mapping: mapping) {
            samples.append(encoded)
          }
        }
        budget -= page.samples.count
        if page.hitLimit { hasMore = true }
      }

      // Workouts.
      if budget > 0 {
        let workoutType = HKObjectType.workoutType()
        let page = self.runAnchored(
          type: workoutType, key: workoutType.identifier, anchors: &anchors,
          backfillStart: backfillStart, limit: min(budget, 200))
        for case let w as HKWorkout in page.samples {
          workouts.append(Self.encodeWorkout(w))
        }
        if page.hitLimit { hasMore = true }
      } else {
        hasMore = true
      }

      let payload: [String: Any] = [
        "samples": samples,
        "workouts": workouts,
        "cursor": Self.encodeCursor(anchors),
        "hasMore": hasMore,
      ]
      DispatchQueue.main.async { result(payload) }
    }
  }

  private struct Page {
    let samples: [HKSample]
    let hitLimit: Bool
  }

  /// One HKAnchoredObjectQuery, waited on synchronously (we are on `work`).
  /// Deleted objects are ignored: the server keeps what it was given.
  private func runAnchored(
    type: HKSampleType, key: String, anchors: inout [String: HKQueryAnchor],
    backfillStart: Date, limit: Int
  ) -> Page {
    let anchor = anchors[key]
    // Only the first pass for a type is bounded; after that the anchor
    // carries the position and the predicate must stay nil so nothing is
    // missed if the clock moves.
    let predicate: NSPredicate? = anchor == nil
      ? HKQuery.predicateForSamples(withStart: backfillStart, end: nil, options: [])
      : nil

    let done = DispatchSemaphore(value: 0)
    var collected: [HKSample] = []
    var newAnchor: HKQueryAnchor? = anchor

    let query = HKAnchoredObjectQuery(
      type: type, predicate: predicate, anchor: anchor, limit: limit
    ) { _, added, _, returnedAnchor, error in
      if error == nil {
        collected = added ?? []
        if let returnedAnchor = returnedAnchor { newAnchor = returnedAnchor }
      }
      done.signal()
    }
    store.execute(query)
    _ = done.wait(timeout: .now() + 60)

    if let newAnchor = newAnchor { anchors[key] = newAnchor }
    return Page(samples: collected, hitLimit: collected.count >= limit && limit > 0)
  }

  // MARK: - Mapping tables (docs/ingest-contract.md, HealthKit column)

  private struct QuantityMapping {
    let metric: String
    let type: HKQuantityType
    let unit: HKUnit
    let contractUnit: String
    /// Multiplier applied after conversion to `unit` (percent is 0..1 in HealthKit).
    let scale: Double
    let interval: Bool
  }

  private struct CategoryMapping {
    let metric: String
    let type: HKCategoryType
    let contractUnit: String
  }

  private static func quantity(_ id: HKQuantityTypeIdentifier) -> HKQuantityType? {
    HKObjectType.quantityType(forIdentifier: id)
  }

  private static let bpm = HKUnit.count().unitDivided(by: .minute())
  private static let mmolPerLitre = HKUnit.moleUnit(
    with: .milli, molarMass: HKUnitMolarMassBloodGlucose
  ).unitDivided(by: .liter())
  private static let vo2Unit = HKUnit.literUnit(with: .milli)
    .unitDivided(by: HKUnit.gramUnit(with: .kilo).unitMultiplied(by: .minute()))

  private static let quantityMappings: [QuantityMapping] = {
    var list: [QuantityMapping] = []
    func add(
      _ metric: String, _ id: HKQuantityTypeIdentifier, _ unit: HKUnit, _ contractUnit: String,
      scale: Double = 1, interval: Bool = false
    ) {
      if let type = quantity(id) {
        list.append(
          QuantityMapping(
            metric: metric, type: type, unit: unit, contractUnit: contractUnit, scale: scale,
            interval: interval))
      }
    }
    add("steps", .stepCount, .count(), "count", interval: true)
    add("distance_m", .distanceWalkingRunning, .meter(), "m", interval: true)
    add("distance_m", .distanceCycling, .meter(), "m", interval: true)
    add("active_energy_kcal", .activeEnergyBurned, .kilocalorie(), "kcal", interval: true)
    add("basal_energy_kcal", .basalEnergyBurned, .kilocalorie(), "kcal", interval: true)
    add("exercise_min", .appleExerciseTime, .minute(), "min", interval: true)
    add("flights", .flightsClimbed, .count(), "count", interval: true)
    add("heart_rate_bpm", .heartRate, bpm, "bpm")
    add("resting_hr_bpm", .restingHeartRate, bpm, "bpm")
    add("hrv_sdnn_ms", .heartRateVariabilitySDNN, .secondUnit(with: .milli), "ms")
    add("spo2_pct", .oxygenSaturation, .percent(), "%", scale: 100)
    add("respiratory_rate_bpm", .respiratoryRate, bpm, "breaths/min")
    add("body_temp_c", .bodyTemperature, .degreeCelsius(), "C")
    add("vo2max", .vo2Max, vo2Unit, "mL/kg/min")
    add("weight_kg", .bodyMass, .gramUnit(with: .kilo), "kg")
    add("body_fat_pct", .bodyFatPercentage, .percent(), "%", scale: 100)
    add("height_cm", .height, .meterUnit(with: .centi), "cm")
    add("basal_body_temp_c", .basalBodyTemperature, .degreeCelsius(), "C")
    add("blood_glucose_mmol", .bloodGlucose, mmolPerLitre, "mmol/L")
    add("blood_pressure_sys", .bloodPressureSystolic, .millimeterOfMercury(), "mmHg")
    add("blood_pressure_dia", .bloodPressureDiastolic, .millimeterOfMercury(), "mmHg")
    if #available(iOS 16.0, *) {
      // HealthKit stores the absolute sleeping wrist temperature; the server
      // derives the deviation from the person's baseline. meta.kind says so.
      add("wrist_temp_delta_c", .appleSleepingWristTemperature, .degreeCelsius(), "C")
    }
    return list
  }()

  private static let categoryMappings: [CategoryMapping] = {
    var list: [CategoryMapping] = []
    if let sleep = HKObjectType.categoryType(forIdentifier: .sleepAnalysis) {
      list.append(CategoryMapping(metric: "sleep_stage", type: sleep, contractUnit: "stage"))
    }
    if let flow = HKObjectType.categoryType(forIdentifier: .menstrualFlow) {
      list.append(CategoryMapping(metric: "menstrual_flow", type: flow, contractUnit: "level"))
    }
    return list
  }()

  // MARK: - Encoding samples

  private static func encodeQuantity(_ q: HKQuantitySample, mapping: QuantityMapping) -> [String: Any] {
    var meta: [String: Any] = [
      "hkType": mapping.type.identifier,
      "sourceBundle": q.sourceRevision.source.bundleIdentifier,
    ]
    if let device = q.device?.name { meta["device"] = device }
    if let motion = q.metadata?[HKMetadataKeyHeartRateMotionContext] as? NSNumber {
      meta["motion"] = motionName(motion.intValue)
    }
    if mapping.metric == "wrist_temp_delta_c" { meta["kind"] = "absolute" }
    let value = q.quantity.doubleValue(for: mapping.unit) * mapping.scale
    return [
      "sourceUid": "hk-" + q.uuid.uuidString,
      "metric": mapping.metric,
      "start": iso(q.startDate),
      "end": mapping.interval ? iso(q.endDate) : NSNull(),
      "value": value,
      "unit": mapping.contractUnit,
      "meta": meta,
    ]
  }

  private static func motionName(_ raw: Int) -> String {
    switch raw {
    case 1: return "sedentary"
    case 2: return "active"
    default: return "unknown"
    }
  }

  private static func encodeCategory(_ c: HKCategorySample, mapping: CategoryMapping) -> [String: Any]? {
    let value: Int
    let label: String
    let labelKey: String
    if mapping.metric == "sleep_stage" {
      guard let stage = sleepStage(rawValue: c.value) else { return nil }
      value = stage.0
      label = stage.1
      labelKey = "stage"
    } else {
      let flow = menstrualFlow(rawValue: c.value)
      value = flow.0
      label = flow.1
      labelKey = "flow"
    }
    var meta: [String: Any] = [
      "hkType": mapping.type.identifier,
      "sourceBundle": c.sourceRevision.source.bundleIdentifier,
    ]
    meta[labelKey] = label
    if let device = c.device?.name { meta["device"] = device }
    return [
      "sourceUid": "hk-" + c.uuid.uuidString,
      "metric": mapping.metric,
      "start": iso(c.startDate),
      "end": iso(c.endDate),
      "value": value,
      "unit": mapping.contractUnit,
      "meta": meta,
    ]
  }

  /// Contract scheme: 0 in_bed, 1 awake, 2 light, 3 deep, 4 rem, 5 unspecified.
  private static func sleepStage(rawValue: Int) -> (Int, String)? {
    guard let stage = HKCategoryValueSleepAnalysis(rawValue: rawValue) else { return nil }
    if #available(iOS 16.0, *) {
      switch stage {
      case .inBed: return (0, "in_bed")
      case .awake: return (1, "awake")
      case .asleepCore: return (2, "light")
      case .asleepDeep: return (3, "deep")
      case .asleepREM: return (4, "rem")
      case .asleepUnspecified: return (5, "unspecified")
      @unknown default: return nil
      }
    }
    switch stage {
    case .inBed: return (0, "in_bed")
    case .awake: return (1, "awake")
    case .asleep: return (5, "unspecified")
    default: return nil
    }
  }

  /// Contract scheme: 0 none, 1 light, 2 medium, 3 heavy, 4 unspecified.
  private static func menstrualFlow(rawValue: Int) -> (Int, String) {
    guard let flow = HKCategoryValueMenstrualFlow(rawValue: rawValue) else {
      return (4, "unspecified")
    }
    switch flow {
    case .light: return (1, "light")
    case .medium: return (2, "medium")
    case .heavy: return (3, "heavy")
    case .none: return (0, "none")
    case .unspecified: return (4, "unspecified")
    @unknown default: return (4, "unspecified")
    }
  }

  private static func encodeWorkout(_ w: HKWorkout) -> [String: Any] {
    var payload: [String: Any] = [
      "sourceUid": "hk-" + w.uuid.uuidString,
      "type": workoutTypeName(w.workoutActivityType),
      "start": iso(w.startDate),
      "end": iso(w.endDate),
      "distanceM": NSNull(),
      "energyKcal": NSNull(),
      "avgHr": NSNull(),
      "maxHr": NSNull(),
      "routeGeoJson": NSNull(),
    ]
    var meta: [String: Any] = [
      "hkActivityType": Int(w.workoutActivityType.rawValue),
      "sourceBundle": w.sourceRevision.source.bundleIdentifier,
      "durationS": w.duration,
    ]
    if let device = w.device?.name { meta["device"] = device }

    if #available(iOS 16.0, *) {
      if let distance = quantity(.distanceWalkingRunning),
        let sum = w.statistics(for: distance)?.sumQuantity()
      {
        payload["distanceM"] = sum.doubleValue(for: .meter())
      } else if let cycling = quantity(.distanceCycling),
        let sum = w.statistics(for: cycling)?.sumQuantity()
      {
        payload["distanceM"] = sum.doubleValue(for: .meter())
      }
      if let energy = quantity(.activeEnergyBurned),
        let sum = w.statistics(for: energy)?.sumQuantity()
      {
        payload["energyKcal"] = sum.doubleValue(for: .kilocalorie())
      }
      if let hr = quantity(.heartRate), let stats = w.statistics(for: hr) {
        if let avg = stats.averageQuantity() { payload["avgHr"] = avg.doubleValue(for: bpm) }
        if let max = stats.maximumQuantity() { payload["maxHr"] = max.doubleValue(for: bpm) }
      }
    }
    // Older systems, or a workout whose statistics are empty: fall back to the
    // totals the workout itself carries.
    if payload["distanceM"] is NSNull, let total = w.totalDistance {
      payload["distanceM"] = total.doubleValue(for: .meter())
    }
    if payload["energyKcal"] is NSNull, let total = w.totalEnergyBurned {
      payload["energyKcal"] = total.doubleValue(for: .kilocalorie())
    }
    payload["meta"] = meta
    return payload
  }

  /// Lower-case names shared with the Android collector where the activities
  /// overlap. Anything else is "other" with the raw type in meta.
  private static func workoutTypeName(_ type: HKWorkoutActivityType) -> String {
    switch type {
    case .running: return "running"
    case .walking: return "walking"
    case .cycling: return "cycling"
    case .swimming: return "swimming"
    case .hiking: return "hiking"
    case .yoga: return "yoga"
    case .rowing: return "rowing"
    case .elliptical: return "elliptical"
    case .stairClimbing: return "stair_climbing"
    case .traditionalStrengthTraining, .functionalStrengthTraining: return "strength_training"
    case .highIntensityIntervalTraining: return "hiit"
    case .pilates: return "pilates"
    case .dance: return "dance"
    case .tennis: return "tennis"
    case .soccer: return "football"
    case .basketball: return "basketball"
    case .golf: return "golf"
    case .crossTraining: return "cross_training"
    case .coreTraining: return "core_training"
    case .flexibility: return "stretching"
    case .mindAndBody: return "mind_and_body"
    case .cooldown: return "cooldown"
    default: return "other"
    }
  }

  // MARK: - Cursor

  private static func decodeCursor(_ cursor: String?) -> [String: HKQueryAnchor] {
    guard let cursor = cursor, let data = cursor.data(using: .utf8),
      let object = try? JSONSerialization.jsonObject(with: data) as? [String: String]
    else { return [:] }
    var anchors: [String: HKQueryAnchor] = [:]
    for (key, base64) in object {
      guard let bytes = Data(base64Encoded: base64) else { continue }
      if let anchor = try? NSKeyedUnarchiver.unarchivedObject(ofClass: HKQueryAnchor.self, from: bytes) {
        anchors[key] = anchor
      }
    }
    return anchors
  }

  private static func encodeCursor(_ anchors: [String: HKQueryAnchor]) -> String? {
    var object: [String: String] = [:]
    for (key, anchor) in anchors {
      if let data = try? NSKeyedArchiver.archivedData(withRootObject: anchor, requiringSecureCoding: true) {
        object[key] = data.base64EncodedString()
      }
    }
    guard let json = try? JSONSerialization.data(withJSONObject: object) else { return nil }
    return String(data: json, encoding: .utf8)
  }

  // MARK: - Dates

  private static let isoFormatter: ISO8601DateFormatter = {
    let f = ISO8601DateFormatter()
    f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
    f.timeZone = TimeZone(secondsFromGMT: 0)
    return f
  }()

  private static let isoPlainFormatter: ISO8601DateFormatter = {
    let f = ISO8601DateFormatter()
    f.formatOptions = [.withInternetDateTime]
    f.timeZone = TimeZone(secondsFromGMT: 0)
    return f
  }()

  private static func iso(_ date: Date) -> String {
    isoFormatter.string(from: date)
  }

  private static func parseIso(_ text: String) -> Date? {
    isoFormatter.date(from: text) ?? isoPlainFormatter.date(from: text)
  }
}
