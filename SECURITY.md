# Security Policy

## Supported Versions

Security updates and bug fixes are applied to the latest release on the `main` branch.

| Version | Supported          |
| ------- | ------------------ |
| 1.0.x   | :white_check_mark: |
| < 1.0   | :x:                |

## Privacy & Audio Architecture Principles

MentionedMe is designed with the following security and privacy boundaries:

- **Loopback Capture Only**: The application captures audio rendered through Windows output endpoints (`DataFlow.Render`). It explicitly does not request or open microphone capture streams (`DataFlow.Capture`).
- **On-Device Inference**: Audio transcription runs 100% locally via embedded `whisper.cpp` libraries. No audio data, PCM frames, or speech transcripts are ever uploaded to cloud endpoints.
- **Ephemeral Buffering**: Audio chunks are held temporarily in RAM during transcription and immediately zeroed/released. No audio is persisted to disk.
- **Local Settings Storage**: Preferences are kept in your local user profile (`%LOCALAPPDATA%\MentionedMe\settings.json`) and never transmitted to external analytics servers.

## Reporting a Vulnerability

If you discover a potential security vulnerability within MentionedMe, please **do not** report it through a public GitHub issue.

Instead, please report security vulnerabilities responsibly:

1. Send an email to the project maintainers or open a [GitHub Private Vulnerability Report](https://github.com/Srinath318/MentionedMe_Desktop/security/advisories/new) if enabled.
2. Provide a detailed summary of the issue, including:
   - Type of vulnerability.
   - Exact steps or proof-of-concept to reproduce.
   - Affected components (Desktop, Relay, or Mobile).
   - Potential impact.
3. We will acknowledge receipt within 48 hours and provide a remediation timeline.
4. Once resolved, we will publish a patch release and credit the reporter if desired.
