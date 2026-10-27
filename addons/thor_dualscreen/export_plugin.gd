# SPDX-License-Identifier: Apache-2.0
# Copies the Java bridge into the gradle build template and registers it as a v2 Godot plugin.
@tool
extends EditorExportPlugin

const JAVA_SOURCE := "res://addons/thor_dualscreen/android/Wa2ThorPlugin.java"
const JAVA_TARGET_DIR := "res://android/build/src/org/wa2/thor"


func _get_name() -> String:
	return "ThorDualScreen"


func _supports_platform(platform: EditorExportPlatform) -> bool:
	return platform is EditorExportPlatformAndroid


func _export_begin(features: PackedStringArray, is_debug: bool, path: String, flags: int) -> void:
	if not features.has("android"):
		return
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(JAVA_TARGET_DIR))
	var source := FileAccess.get_file_as_string(JAVA_SOURCE)
	var out := FileAccess.open(JAVA_TARGET_DIR.path_join("Wa2ThorPlugin.java"), FileAccess.WRITE)
	if out == null:
		push_error("ThorDualScreen: cannot write Java bridge")
		return
	out.store_string(source)
	out.close()
	_patch_gradle()


## Kuromoji's core and dictionary jars both ship META-INF/*.md; keep one copy.
func _patch_gradle() -> void:
	const GRADLE := "res://android/build/build.gradle"
	const MARKER := "// thor_dualscreen: kuromoji metadata"
	var text := FileAccess.get_file_as_string(GRADLE)
	if text.is_empty() or text.contains(MARKER):
		return
	var anchor := "exclude 'META-INF/NOTICE'"
	if not text.contains(anchor):
		push_warning("ThorDualScreen: build.gradle layout changed; META-INF merge rule not added")
		return
	text = text.replace(anchor, anchor + "\n        " + MARKER + "\n        pickFirst 'META-INF/*.md'\n        pickFirst 'META-INF/*.txt'")
	var f := FileAccess.open(GRADLE, FileAccess.WRITE)
	f.store_string(text)
	f.close()


func _get_android_dependencies(platform: EditorExportPlatform, debug: bool) -> PackedStringArray:
	# Japanese tokenizer with the IPADIC dictionary: readings for furigana on the lower screen
	return PackedStringArray(["com.atilika.kuromoji:kuromoji-ipadic:0.9.0"])


func _get_android_manifest_application_element_contents(platform: EditorExportPlatform, debug: bool) -> String:
	return "\t\t<meta-data android:name=\"org.godotengine.plugin.v2.Wa2Thor\" android:value=\"org.wa2.thor.Wa2ThorPlugin\" />\n"


## AnkiDroid's provider permission, and package visibility (Android 11+) for AnkiDroid and the
## system text-to-speech engines (without the TTS_SERVICE query TextToSpeech finds no engine).
func _get_android_manifest_element_contents(platform: EditorExportPlatform, debug: bool) -> String:
	return "\t<uses-permission android:name=\"com.ichi2.anki.permission.READ_WRITE_DATABASE\" />\n\t<queries>\n\t\t<package android:name=\"com.ichi2.anki\" />\n\t\t<intent>\n\t\t\t<action android:name=\"android.intent.action.TTS_SERVICE\" />\n\t\t</intent>\n\t</queries>\n"
