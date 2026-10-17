[简体中文](README.md) | **English**

<div align="center">

# WHITE ALBUM 2 DS

**WHITE ALBUM2 · AYN Thor dual-screen edition**

Play on the top screen; the bottom screen is a companion built from the game's own UI art<br>
Backlog · Japanese original with furigana · Word lookup · AnkiDroid word list · Favourite lines

![AYN Thor](https://img.shields.io/badge/AYN%20Thor-dual%20screen-2aa7b8)
![Android](https://img.shields.io/badge/Android-arm64-3ddc84?logo=android&logoColor=white)
![Godot](https://img.shields.io/badge/Godot-4.5%20.NET-478cbf?logo=godotengine&logoColor=white)
[![Fork](https://img.shields.io/badge/fork%20of-dorakyuraduang%2Fwa2--godot-555)](https://github.com/dorakyuraduang/wa2-godot)
![License](https://img.shields.io/badge/code-Apache--2.0-blue)

<br>

<img src="https://github.com/user-attachments/assets/6c50b2c8-864c-4cc9-af77-41e4dd6b145e" width="62%" alt="AYN Thor: backlog with the Japanese original and furigana" />

<sub>On the AYN Thor: the bottom screen shows the backlog, with the Japanese original of the current line pinned on top and furigana over the kanji</sub>

<br><br>

<table>
  <tr>
    <td align="center" width="50%">
      <img src="https://github.com/user-attachments/assets/f800ffa4-eea4-4122-adb3-a36c4abc4539" alt="Title screen and quick commands" /><br>
      <sub>Title screen · Quick commands</sub>
    </td>
    <td align="center" width="50%">
      <img src="https://github.com/user-attachments/assets/2b4072bc-a662-4dcf-bf45-4a2f76711d87" alt="In story and favourites" /><br>
      <sub>In story · Favourites</sub>
    </td>
  </tr>
</table>

</div>

A fork of [dorakyuraduang/wa2-godot](https://github.com/dorakyuraduang/wa2-godot) (the WHITE ALBUM2 Godot 4 port) for the AYN Thor. The bottom screen is assembled from the game's own page header, backlog column, scrollbar, buttons and Leaf footer, so it looks like one more page of the game.

It targets the Simplified Chinese (CK-GAL) translation that the upstream port plays: the story on the top screen is in Chinese, the Japanese original is shown on the bottom screen, and the bottom-screen menus are in Chinese.

## ✨ Bottom-screen pages

- 📜 **Backlog (文本记录)**: newest line first; the line currently on the top screen is not repeated. Tap the voice icon to replay the recorded voice, tap a line to look up its words, long-press to add it to favourites.
- ⭐ **Favourites (收藏)**: favourite lines (Chinese, Japanese original and voice) and the word list from lookups, which can be sent to AnkiDroid.
- 🎮 **Quick commands (快捷操作)**: save, load, quick save, quick load, settings, auto, skip, hide text, back, return to title. When a choice appears the screen switches here so you can pick it directly.
- 📖 **Guide (操作说明)**: controls and settings: text size, Japanese original, furigana, automatic system voice.

Like every in-game page, the footer shows the current BGM title, with its level meter moving with the music.

## 📚 Learning Japanese with WHITE ALBUM2

- **Japanese original**: the Chinese patch keeps the order of the original script, so every Chinese line has its Japanese counterpart, drawn with the game's own Japanese font.
- **Furigana**: readings come from on-device tokenizing and are written in the game's own ruby format; ruby that the original script already has is kept as is.
- **Word lookup**: splits a Japanese line into words with reading, part of speech and meaning (Chinese Wiktionary first, JMdict English as a fallback). Works offline.
- **AnkiDroid word list**: one tap creates a card with the meaning and an example sentence with furigana, in a deck named "WHITE ALBUM2".
- **System voice**: narration and lines without a recorded voice can be read aloud in Japanese with Android's text-to-speech.

## 🎮 Controller

| Where | Controls |
| --- | --- |
| In story | **A** advance　**B** back　**X** auto　**Y** hide text　hold **R1** skip　**L1** switch bottom-screen page　**D-pad up/down** scroll the backlog |
| Title and menus | **D-pad / left stick** move between buttons　**A** confirm　**B** back |

Touch still works on both screens.

## 📦 Installation

1. Install the `WHITE ALBUM 2 DS` APK (package `com.example.wa2.thor`; it can be installed next to the upstream port's APK).
2. Game data is set up exactly as for the upstream port: copy your own PC game folder to the root of the device storage and name it `Wa2Res` (see the upstream notes below). The Japanese original needs the original `script.pak` inside `Wa2Res`.
3. Grant file access on first launch. The first time you send a word to AnkiDroid, allow access in the system dialog on the top screen.

## 🔧 Building and implementation

Implementation notes, build steps (including how to generate the offline dictionary) and third-party data licences are in [addons/thor_dualscreen/README.md](addons/thor_dualscreen/README.md) (Chinese). This fork has only been tested on the AYN Thor.

## 📄 Licence

The porting code is open under Apache-2.0 as set out by upstream (see [LICENSE](LICENSE), [NOTICE](NOTICE) and [PORTING_CODE.md](PORTING_CODE.md)); upstream files changed by this fork state their changes at the top of the file. Lookup data comes from JMdict (EDRDG) and Chinese Wiktionary under CC BY-SA and is not distributed with the repository. The original WHITE ALBUM2 data and rights are not provided here; use your own legal copy, see [ASSET_POLICY.md](ASSET_POLICY.md).

<details>
<summary><b>Upstream notes (dorakyuraduang/wa2-godot)</b></summary>

## Game Installation (Android)

1. Download and install the APK.
2. Copy the entire PC game directory to the root of your phone's storage.
3. Rename the copied directory to `Wa2Res`.

## Android Controls

1. Press and hold anywhere on the screen to fast-forward. This is disabled by default and becomes available after changing the display mode to Fullscreen in Settings.
2. Press the Back button during gameplay to open the return-to-main-menu confirmation dialog.

## Important Notes

1. You must obtain the PC game files yourself.
2. Do not place the data inside additional nested directories. The game data must be directly inside `Wa2Res` to avoid path errors.
3. To update the app, install the latest APK over the existing version. You do not need to copy the data again.
4. Uninstalling the app does not automatically remove the `Wa2Res` data directory. Delete it manually from the storage root if you want to remove it completely.
5. Grant file access permission on first launch. The application cannot read local resources without this permission.
6. If the game freezes after running for some time, try changing the display mode to Fullscreen in Settings.

Example directory layout:

![Wa2Res directory layout](https://github.com/user-attachments/assets/303a7d6e-99e1-4683-bed9-671d271e08be)

## Video Playback Plugin

The project uses the FFmpeg GDExtension in `addons/wmv_video` to play the original `.pak` movie files. The plugin accepts only ASF content containing WMV1, WMV2, WMV3, or VC-1 video. File names may use either the `.wmv` or `.pak` extension.

- Windows: x86_64
- Android: `arm64-v8a`, minimum API level 24
- FFmpeg: 7.1.5, minimal LGPL-2.1-or-later shared build

The Windows and Android native libraries are included in the plugin's `bin` directory. The Android export preset automatically packages the GDExtension and its shared FFmpeg dependencies into the APK.

## Licensing and Asset Boundaries

This repository is not covered by a single project-wide MIT license. See [LICENSE](LICENSE) for the exact scope:

- Contributor-authored resource readers, shaders, Godot scenes, animation configuration, font mapping, and project configuration explicitly listed in [PORTING_CODE.md](PORTING_CODE.md) are licensed under Apache License 2.0. Anyone may fork, modify, port, and redistribute them without opening a pull request, requesting separate approval, or contributing changes back to this repository.
- Contributor-authored plugin source, build tools, and documentation under `addons/wmv_video` are licensed under the MIT License in that directory.
- FFmpeg, godot-cpp, libwinpthread, and YamlDotNet remain subject to their respective licenses.
- `assets/sub.yaml` contains mixed third-party subtitle data credited to Moegirlpedia and CK-GAL. It is not covered by Apache-2.0. See [SUBTITLE_NOTICE.md](SUBTITLE_NOTICE.md) for details.
- This repository grants no permission to copy, modify, redistribute, or sell files that are not explicitly covered by `PORTING_CODE.md`, a component license, or a file-level SPDX identifier.
- Source availability alone does not imply an open-source or redistribution license.

This repository does not license the original WHITE ALBUM2 software, data, story, movies, audio, images, fonts, trademarks, or content extracted, transcribed, translated, converted, or decompiled from them. Content without sufficient redistribution permission must not be committed or published. Users must provide legally obtained original game resources locally. See [ASSET_POLICY.md](ASSET_POLICY.md) for repository rules and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for third-party component versions and licenses.

## Porting to Other Platforms

Anyone may directly fork, modify, port, and publish the Apache-2.0 files listed in [PORTING_CODE.md](PORTING_CODE.md). No pull request, separate approval, or contribution of modifications back to this repository is required. Redistributions must include the Apache-2.0 license and [NOTICE](NOTICE), retain applicable copyright and attribution notices, and identify changes in modified files.

Recommended porting workflow:

1. Open the project with the Godot 4.5 .NET edition and install the Godot export templates, compiler, and platform SDK required by the target platform.
2. Do not place original game data in the repository or application package. A port should load resources prepared locally by the user from a legally obtained copy of the game.
3. Build `addons/wmv_video` for the target operating system and CPU architecture. The plugin requires matching godot-cpp libraries, FFmpeg 7.1.5 development libraries, and FFmpeg runtime libraries.
4. Confirm that `addons/wmv_video/wmv_video.gdextension` contains an entry for the target platform, and enable only architectures with available native libraries in the Godot export preset.

Unified build entry points:

```powershell
# Windows x86_64
addons\wmv_video\tools\build.ps1 --platform windows --target both

# Android ARM64; replace the NDK path with the local installation path
addons\wmv_video\tools\build.ps1 --platform android --arch arm64 --android-ndk D:\Android\ndk\25.2.9519653
```

```bash
# Linux x86_64
bash addons/wmv_video/tools/build.sh --platform linux --arch x86_64

# macOS universal
bash addons/wmv_video/tools/build.sh --platform macos --arch universal

# iOS device and simulator XCFramework
bash addons/wmv_video/tools/build.sh --platform ios
```

For dependency locations, FFmpeg SDK layout, optional arguments, and LGPL considerations, see the [Chinese build guide](addons/wmv_video/BUILDING.zh-CN.md) and the [complete build guide](addons/wmv_video/BUILDING.md). Native binaries have currently been produced only for Windows x86_64 and Android ARM64. Android APK packaging has been verified, but no physical-device test result has been recorded. Linux, macOS, iOS, and Android x86_64 remain build configurations that must be compiled and tested on their respective systems.

Port maintainers may change the project name, interface, and implementation or create an independent repository, but Apache-2.0 covers only the original porting code in the authorized file list. `addons/wmv_video` remains under its directory-level MIT License. FFmpeg, godot-cpp, YamlDotNet, and other third-party components remain subject to their own licenses. `assets/sub.yaml` requires separate handling because it includes third-party sources with noncommercial or unidentified licensing. No rights to original game assets are transferred with this repository's code.

</details>
