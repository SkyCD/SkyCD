using System;

namespace SkyCD.Plugin.Runtime.Exceptions;

public sealed class PluginDocumentRepositoryTypeMismatchException()
    : InvalidOperationException("Repository for PluginDocument must be PluginRepository.");
