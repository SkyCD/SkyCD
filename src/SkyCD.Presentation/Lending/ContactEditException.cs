using System;

namespace SkyCD.Presentation.Lending;

internal sealed class ContactEditException() : InvalidOperationException("The contact could not be updated with the provided values.");
