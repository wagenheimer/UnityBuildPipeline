# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-09-07

### Added
- Initial release of `com.wagenheimer.buildpipeline`.
- Unified runtime `GameConfig` ScriptableObject supporting all Green Sauce Games properties.
- Unified stable `Publisher` and `GameLanguage` enums with language code/name extension methods.
- Modular pipeline engine with extensible `IPreBuildStep` and `IPostBuildStep` lifecycle.
- Built-in steps: GameConfig synchronization, PlayerSettings configuration, Android Keystore setup, Scene filtering, Publisher splash copier, Crash handler cleanup, and Zip archiving.
- Modern UI Toolkit EditorWindow with Quick Build tabs, Matrix batch generator, and live CLI command exporter.
- Headless `BuildCLI` runner supporting Unity CLI `-batchmode -quit` and PowerShell automation.
- 1-click Legacy GameConfig Migration wizard.
