# Contributing to MentionedMe

Thank you for your interest in contributing to **MentionedMe**! We are building a privacy-first, on-device audio notification tool that empowers users during virtual meetings.

Whether you're fixing bugs, adding new features, improving documentation, or optimizing the speech-to-text pipeline, your contributions are welcome and greatly appreciated.

---

## Code of Conduct

By participating in this project, you agree to abide by our [Code of Conduct](CODE_OF_CONDUCT.md). Please treat all contributors with respect, courtesy, and empathy.

---

## How Can I Contribute?

### 1. Reporting Bugs

Before creating a bug report, check the existing issues to ensure the problem has not already been reported.

When opening an issue, please use the **Bug Report** template and include:
- A clear, descriptive title.
- Steps to reproduce the problem.
- Expected behavior vs. actual behavior.
- Your Windows version (Windows 10 / 11 and build number).
- Audio hardware configuration (e.g., Realtek speakers, Bluetooth headset, USB DAC).
- Relevant log output or error messages shown in the error banner.

### 2. Suggesting Features & Enhancements

We welcome proposals for new capabilities! Please open an issue using the **Feature Request** template to discuss your idea before investing substantial time in implementation.

Key criteria for feature consideration:
- **Privacy First**: Does it respect zero-cloud-audio guarantees?
- **Low Footprint**: Does it avoid excessive CPU, GPU, or battery drain?
- **User Control**: Is the behavior configurable and non-intrusive?

### 3. Submitting Pull Requests

1. **Fork the Repository**: Create your personal fork of `MentionedMe_Desktop`.
2. **Branch from `main`**:
   ```bash
   git checkout -b feature/your-feature-name
   # or
   git checkout -b fix/your-bugfix-name
   ```
3. **Keep Changes Focused**: Each pull request should address a single concern.
4. **Adhere to Code Standards**:
   - Follow standard C# coding conventions and Microsoft .NET style guidelines.
   - For UI elements, use predefined styles and Fluent color palettes matching `MainWindow.xaml`.
   - Ensure nullability annotations (`#nullable enable`) are respected.
   - Do NOT add external dependencies without prior discussion.
5. **Run Tests Locally**:
   Ensure all tests compile and pass before submitting:
   ```powershell
   dotnet test tests\MentionedMe.Tests\MentionedMe.Tests.csproj
   ```
6. **Commit Messages**: Write clear, imperative commit messages (e.g., `Add support for multiple output audio endpoints`, `Fix regex possessive matching edge case`).
7. **Open a PR**: Submit your pull request against the `main` branch.

---

## Local Development Setup

### Desktop Application (.NET 8 WPF)
1. Install [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
2. Open `MentionedMe.csproj` in Visual Studio 2022 (with .NET desktop development workload) or JetBrains Rider or VS Code with C# Dev Kit.
3. Build the solution:
   ```powershell
   dotnet build -c Debug
   ```
4. Run tests:
   ```powershell
   dotnet test tests\MentionedMe.Tests\MentionedMe.Tests.csproj
   ```

### Push Relay Server (Node.js)
1. Navigate to the `relay/` directory:
   ```bash
   cd relay
   npm install
   npm run dev
   ```
2. The server runs with auto-reload (`node --watch server.js`) on port 3000.

### Mobile Companion App (React Native / Expo)
1. Navigate to the `mobile/` directory:
   ```bash
   cd mobile
   npm install
   npx expo start
   ```

---

## Architectural Principles

When writing or reviewing code for MentionedMe, keep these core principles in mind:

- **Strict Audio Isolation**: WASAPI Loopback capture must never open input/recording endpoints (`DataFlow.Capture`). We only ever capture rendered output (`DataFlow.Render`).
- **RAM Efficiency**: Audio streams are processed as small, discrete chunks and resampled in-memory. Never buffer hours of uncompressed WAV data in RAM or write audio frames to disk.
- **Deduplication Integrity**: Always test matching logic against overlapping transcript chunks. Whisper frequently repeats words across rolling transcription boundaries.
- **Thread Safety**: UI updates must be dispatched to the WPF `Dispatcher`. Audio callbacks and transcription worker threads must remain non-blocking.

---

## License

By contributing to MentionedMe, you agree that your contributions will be licensed under the project's [MIT License](LICENSE).
