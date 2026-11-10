# Miko Apps

A growing collection of sample applications showcasing UI designs, styles, and application scenarios built with [Miko](https://github.com/chaldea/miko), a native, cross-platform UI framework for .NET. Miko applications use Razor components and are rendered natively with SkiaSharp.

This repository provides practical examples of how to build different types of applications with Miko. Each app lives in its own directory and demonstrates reusable layouts, styling, interactions, and application structure.

## Applications

| Application | Description | Documentation |
| --- | --- | --- |
| [Anime](Anime/) | A native anime app demonstrating catalog browsing, search, rankings, weekly schedules, title details, episode selection, video playback, favorites, and account screens with Miko.Ionic. Includes Desktop, Android, iOS, and Simulator host projects. | [App guide](Anime/README.md) |

To list a new sample, add a row to this table with its directory, a short description, and a link to its README. Keep app-specific setup, features, and limitations in that app's README so this table remains the central application index.

## Repository Structure

```text
miko/                   Miko framework source (Git submodule)
<AppName>/               An individual sample application
  MikoApp.<AppName>/     Shared Razor pages, styles, and application logic
  ...Desktop/           Desktop host, when provided
  ...Android/           Android host, when provided
  ...iOS/               iOS host, when provided
  ...Simulator/         Device preview host, when provided
Directory.Build.targets Shared Miko Razor compiler configuration
Startup.props           Shared platform host dependencies
global.json             .NET SDK selection
miko-apps.slnx           Repository solution
```

The samples currently reference Miko's source projects directly through the `miko` submodule. Available platform hosts vary by application; some samples may target desktop only.

## Getting Started

Install the .NET SDK specified in [global.json](global.json), along with the workloads and platform tools required by the sample you want to run.

After cloning the repository, initialize the framework submodule from the repository root:

```sh
git submodule update --init --recursive
```

Build the solution:

```sh
dotnet build
```

Open an app's guide from the application table for its run commands, platform requirements, and demo configuration. Building the entire solution requires the tooling for its included platform projects; you can also build an individual host project for the platform you are working on.

## License and Third-Party Resources

### Miko and Sample Application Code

Miko is licensed under the MIT License, and this repository also adopts the [MIT License](LICENSE). All original sample application code in this repository was created by the Miko team and is available under MIT.

You may use, copy, modify, merge, publish, distribute, sublicense, and sell the Miko-related code and original sample application code, including in commercial projects, subject to the MIT License. Retain the applicable copyright and permission notices when redistributing copies or substantial portions of the code. See [Miko's license](miko/LICENSE) for the framework's notices.

### Third-Party APIs, Services, and Assets

**This repository's MIT License covers its original code; it does not grant rights to third-party APIs, services, assets, or content used by the demos.**

Excluded third-party material includes API endpoints and access to external services, API responses and data, anime titles and descriptions, posters, illustrations, logos, images, fonts, audio, video, and other resources owned by third parties. This applies whether the material is bundled in the repository, embedded in an application, downloaded at runtime, cached locally, or used in an offline preview.

These resources remain subject to their respective owners' licenses, terms of service, and permissions. Their inclusion or use in a demo does not grant permission to reuse, redistribute, or commercially exploit them. The MIT license for our API client and integration code does not grant authorization to access or redistribute the services or content that code connects to. Third-party software dependencies likewise remain subject to their own licenses.

### Packaging and Distribution

**Do not package and distribute a demo app as-is without first obtaining the necessary rights to all third-party assets, content, and API or service usage involved. The project's MIT License is not authorization to distribute the complete demo with those resources or integrations.**

Before distributing an application based on these samples:

- Obtain the required licenses or permissions directly from the relevant asset owners and API or service providers, including any rights needed to bundle and redistribute their content.
- Remove or replace third-party assets and integrations for which you do not have the necessary rights, using your own resources or appropriately licensed alternatives.
- Comply with the applicable third-party terms and attribution requirements, and retain the MIT notices for the code you reuse.

You are responsible for securing these permissions. The Miko team provides the example code under MIT and does not grant or obtain third-party rights on your behalf.
