# R8 rules for the release build.
#
# The ML Kit text recognition plugin references the Chinese, Devanagari, Japanese and Korean
# recognisers, which ship as separate artifacts. Lab reports are read with the Latin one only, so
# those classes are absent on purpose.
-dontwarn com.google.mlkit.vision.text.chinese.**
-dontwarn com.google.mlkit.vision.text.devanagari.**
-dontwarn com.google.mlkit.vision.text.japanese.**
-dontwarn com.google.mlkit.vision.text.korean.**

# WorkManager (background Health Connect sync) keeps its queue in a Room database that Room
# creates by reflection through the generated class's no-argument constructor. R8 removed that
# constructor, so the release build crashed at launch before any screen appeared.
-keep class * extends androidx.room.RoomDatabase { <init>(); }
