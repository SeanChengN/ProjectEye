# Third-party notices — ProjectEye Enhanced 1.0.0

ProjectEye upstream: Planshit/ProjectEye 1.4.3, MIT, Copyright (c) 2020 Plan shit. The complete upstream LICENSE is included unchanged. Enhanced changes are a derivative of that project.

Existing NuGet versions are preserved except the compatible security fixes Newtonsoft.Json 13.0.3 and SharpZipLib 1.3.3. License declarations were checked against restored package nuspec files; package-supplied or upstream license texts are in licenses/. No Windows SDK metadata or reference assemblies are distributed. The .NET Framework and Windows media APIs are supplied by Windows and are not bundled.

| Component | Version | License / source |
| --- | --- | --- |
| EntityFramework | 6.3.0 | Apache-2.0 (package declaration) |
| Newtonsoft.Json | 13.0.3 | MIT; package LICENSE.md |
| NPOI | 2.5.1 | Apache-2.0; package LICENSE |
| Npoi.Mapper | 3.5.1 | MIT; upstream donnytian/Npoi.Mapper LICENSE (nuspec omits license) |
| Portable.BouncyCastle | 1.8.6 | Bouncy Castle license; upstream release-1.8.6 crypto/License.html |
| SharpZipLib | 1.3.3 | MIT; upstream v1.3.3 LICENSE.txt |
| SQLite.CodeFirst | 1.5.3.29 | Apache-2.0; upstream msallin/SQLiteCodeFirst LICENSE |
| System.Data.SQLite / Core / EF6 / Linq | 1.0.112 / 1.0.112.1 | Public domain declaration, https://www.sqlite.org/copyright.html |
| WpfAnimatedGif | 2.0.0 | Apache-2.0 (package declaration) |
| Costura.Fody | 4.0.0 | MIT (package declaration); runtime loader woven into assemblies |

Fody 5.0.6 (MIT) is a build tool, not a distributed application dependency. Project1.UI and ProjectEyeBug originate in the upstream project. Original non-font artwork and sound resources are unchanged from the MIT-licensed upstream source at 50519089. See docs/RELEASE-ASSET-AUDIT.md in the source distribution for the per-file origin inventory. Microsoft font files are excluded; glyphs use the user's installed Windows Segoe MDL2 Assets font, which is not redistributed.

The known Json.NET GHSA-5crp-9r3c-p9vr and SharpZipLib GHSA-m22m-h4rf-pwq3 advisories are fixed by the versions above. Dependency feed checks are point-in-time checks, not a guarantee against future vulnerabilities.

## 安装器简体中文翻译

ChineseSimplified-6.2.2.isl为本机构建工具所用Inno Setup 6.2.2匹配翻译，保留文件头作者与来源：https://github.com/kira-96/Inno-Setup-Chinese-Simplified-Translation 。从本地既有版本复制并固定随仓库，编译时不联网获取。
