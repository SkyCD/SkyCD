using System;

namespace SkyCD.Presentation.Lending;

internal sealed class LoanEditException() : InvalidOperationException("The loan or borrower could not be updated with the provided values.");
