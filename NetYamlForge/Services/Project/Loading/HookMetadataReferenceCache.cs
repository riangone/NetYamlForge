using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace NetYamlForge.Services.Project.Loading;

public class HookMetadataReferenceCache
{
    private readonly ILogger<HookMetadataReferenceCache> _logger;
    private static List<MetadataReference>? _cachedReferences;
    private static readonly object _refLock = new();

    public HookMetadataReferenceCache(ILogger<HookMetadataReferenceCache> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<MetadataReference> GetMetadataReferences()
    {
        lock (_refLock)
        {
            if (_cachedReferences != null)
            {
                return _cachedReferences.ToList();
            }

            var references = new List<MetadataReference>();
            var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (assembly.IsDynamic) continue;

                    if (!string.IsNullOrEmpty(assembly.Location))
                    {
                        if (addedPaths.Add(assembly.Location))
                        {
                            references.Add(MetadataReference.CreateFromFile(assembly.Location));
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("アセンブリ参照の追加中にエラー（スキップ）：{Name}, {Message}", assembly.FullName, ex.Message);
                }
            }

            try
            {
                var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
                var stdLibs = new[] { "System.Runtime.dll", "System.Collections.dll", "System.Runtime.Extensions.dll", "System.Linq.dll", "System.Text.RegularExpressions.dll", "Microsoft.CSharp.dll", "System.IO.Compression.dll", "System.IO.Compression.ZipFile.dll", "System.Text.Json.dll", "System.Xml.Linq.dll", "System.Net.Http.dll" };
                foreach (var lib in stdLibs)
                {
                    var fullPath = Path.Combine(assemblyPath, lib);
                    if (File.Exists(fullPath) && addedPaths.Add(fullPath))
                    {
                        references.Add(MetadataReference.CreateFromFile(fullPath));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load standard system DLL references for hook compilation.");
            }

            try
            {
                var identityAssembly = typeof(Microsoft.AspNetCore.Identity.PasswordHasher<>).Assembly;
                if (!string.IsNullOrEmpty(identityAssembly.Location))
                {
                    if (addedPaths.Add(identityAssembly.Location))
                    {
                        references.Add(MetadataReference.CreateFromFile(identityAssembly.Location));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load Identity DLL references for hook compilation.");
            }

            try
            {
                var imageSharpAssembly = typeof(SixLabors.ImageSharp.Image).Assembly;
                _logger.LogInformation("ImageSharp Assembly Location is: {Location}", imageSharpAssembly.Location);
                if (!string.IsNullOrEmpty(imageSharpAssembly.Location))
                {
                    if (addedPaths.Add(imageSharpAssembly.Location))
                    {
                        references.Add(MetadataReference.CreateFromFile(imageSharpAssembly.Location));
                        _logger.LogInformation("Successfully added ImageSharp metadata reference from: {Location}", imageSharpAssembly.Location);
                    }
                }
                else
                {
                    _logger.LogWarning("ImageSharp Assembly Location is null or empty!");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ImageSharp 参照の强制追加中にエラーが発生しました");
            }

            try
            {
                var httpFeaturesAssembly = typeof(Microsoft.AspNetCore.Http.IHeaderDictionary).Assembly;
                if (!string.IsNullOrEmpty(httpFeaturesAssembly.Location))
                {
                    if (addedPaths.Add(httpFeaturesAssembly.Location))
                    {
                        references.Add(MetadataReference.CreateFromFile(httpFeaturesAssembly.Location));
                        _logger.LogInformation("Successfully added HttpFeatures metadata reference from: {Location}", httpFeaturesAssembly.Location);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HttpFeatures 参照の強制追加中にエラーが発生しました（スキップ）");
            }

            try
            {
                var mailKitAssembly = typeof(MailKit.Net.Imap.ImapClient).Assembly;
                if (!string.IsNullOrEmpty(mailKitAssembly.Location))
                {
                    if (addedPaths.Add(mailKitAssembly.Location))
                    {
                        references.Add(MetadataReference.CreateFromFile(mailKitAssembly.Location));
                        _logger.LogInformation("Successfully added MailKit metadata reference from: {Location}", mailKitAssembly.Location);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MailKit 参照の強制追加中にエラーが発生しました（スキップ）");
            }

            try
            {
                var mimeKitAssembly = typeof(MimeKit.MimeMessage).Assembly;
                if (!string.IsNullOrEmpty(mimeKitAssembly.Location))
                {
                    if (addedPaths.Add(mimeKitAssembly.Location))
                    {
                        references.Add(MetadataReference.CreateFromFile(mimeKitAssembly.Location));
                        _logger.LogInformation("Successfully added MimeKit metadata reference from: {Location}", mimeKitAssembly.Location);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MimeKit 参照の強制追加中にエラーが発生しました（スキップ）");
            }

            _cachedReferences = references;
            return _cachedReferences.ToList();
        }
    }
}
