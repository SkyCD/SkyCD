using System;

namespace SkyCD.App.Exceptions;

public sealed class CatalogRepositoryTypeMismatchException()
    : InvalidOperationException("Catalog document repository must be CatalogDocumentRepository.");