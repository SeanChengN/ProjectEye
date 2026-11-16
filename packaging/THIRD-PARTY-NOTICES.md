# Third-party notices — local test package

ProjectEye upstream: Planshit/ProjectEye 1.4.3, MIT, Copyright (c) 2020 Plan shit. The complete upstream LICENSE is included unchanged. Enhanced changes are a derivative of that project.

Existing NuGet versions are preserved. License declarations were checked against restored package nuspec files; package-supplied or upstream license texts are in licenses/. No Windows SDK metadata or reference assemblies are distributed. The .NET Framework and Windows media APIs are supplied by Windows and are not bundled.

| Component | Version | License / source |
| --- | --- | --- |
| EntityFramework | 6.3.0 | Apache-2.0 (package declaration) |
| Newtonsoft.Json | 12.0.2 | MIT; package LICENSE.md |
| NPOI | 2.5.1 | Apache-2.0; package LICENSE |
| Npoi.Mapper | 3.5.1 | MIT; upstream donnytian/Npoi.Mapper LICENSE (nuspec omits license) |
| Portable.BouncyCastle | 1.8.6 | Bouncy Castle license; upstream release-1.8.6 crypto/License.html |
| SharpZipLib | 1.2.0 | MIT; upstream v1.2.0 LICENSE.txt |
| SQLite.CodeFirst | 1.5.3.29 | Apache-2.0; upstream msallin/SQLiteCodeFirst LICENSE |
| System.Data.SQLite / Core / EF6 / Linq | 1.0.112 / 1.0.112.1 | Public domain declaration, https://www.sqlite.org/copyright.html |
| WpfAnimatedGif | 2.0.0 | Apache-2.0 (package declaration) |
| Costura.Fody | 4.0.0 | MIT (package declaration); runtime loader woven into assemblies |

Fody 5.0.6 (MIT) is a build tool, not a distributed application dependency. Project1.UI and ProjectEyeBug originate in the upstream project. Original artwork/resources are retained under the upstream repository's distribution; a separate asset-by-asset provenance audit remains a formal-release task.

This package is for local testing. Source license notices do not establish that every dependency has passed a security audit; Newtonsoft.Json 12.0.2 has an outstanding high-severity advisory (GHSA-5crp-9r3c-p9vr).
