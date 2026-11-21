using System;

namespace SkyCD.Plugin.Feature.Loans;

internal sealed class ContactEditException() : InvalidOperationException("The contact could not be updated with the provided values.");
