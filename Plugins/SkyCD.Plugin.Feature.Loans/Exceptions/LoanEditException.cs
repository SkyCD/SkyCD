using System;

namespace SkyCD.Plugin.Feature.Loans.Exceptions;

internal sealed class LoanEditException() : InvalidOperationException("The loan or borrower could not be updated with the provided values.");
