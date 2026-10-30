import '../utils/json_reader.dart';

/// The brand block the server reports in /system/version. Used to confirm the
/// client and server were generated from the same brand.yaml; the client's own
/// strings come from [Brand], not from here.
class BrandInfo {
  final String productName;
  final String shortName;
  final String tagline;
  final String supportUrl;
  final String privacyUrl;

  const BrandInfo({
    this.productName = '',
    this.shortName = '',
    this.tagline = '',
    this.supportUrl = '',
    this.privacyUrl = '',
  });

  factory BrandInfo.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return BrandInfo(
      productName: r.string('productName'),
      shortName: r.string('shortName'),
      tagline: r.string('tagline'),
      supportUrl: r.string('supportUrl'),
      privacyUrl: r.string('privacyUrl'),
    );
  }
}

/// Answer of GET /system/version.
class VersionInfo {
  final String version;
  final String minClientVersion;
  final BrandInfo brand;
  final List<String> features;

  const VersionInfo({
    required this.version,
    this.minClientVersion = '',
    this.brand = const BrandInfo(),
    this.features = const [],
  });

  bool hasFeature(String name) => features.contains(name);

  factory VersionInfo.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    final brandJson = r.object('brand');
    return VersionInfo(
      version: r.string('version'),
      minClientVersion: r.string('minClientVersion'),
      brand: brandJson == null
          ? const BrandInfo()
          : BrandInfo.fromJson(brandJson),
      features: r.strings('features'),
    );
  }
}

/// Answer of GET /system/setup-status.
class SetupStatus {
  /// True until the first account exists. That account becomes admin.
  final bool needsFirstUser;

  /// True while anyone may register without an invite code.
  final bool registrationOpen;

  const SetupStatus({
    required this.needsFirstUser,
    required this.registrationOpen,
  });

  factory SetupStatus.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return SetupStatus(
      needsFirstUser: r.boolean('needsFirstUser'),
      registrationOpen: r.boolean('registrationOpen'),
    );
  }
}

/// Answer of GET /system/health.
class HealthStatus {
  final String status;
  final String database;

  const HealthStatus({this.status = '', this.database = ''});

  bool get isOk => status == 'ok' && database == 'ok';

  factory HealthStatus.fromJson(Map<String, dynamic> json) {
    final r = JsonReader(json);
    return HealthStatus(
      status: r.string('status'),
      database: r.string('database'),
    );
  }
}
