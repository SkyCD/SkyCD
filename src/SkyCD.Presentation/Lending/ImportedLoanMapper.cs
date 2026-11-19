using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SkyCD.Documents;

namespace SkyCD.Presentation.Lending;

public sealed class ImportedLoanMapper(CatalogLoanService service)
{
    public IReadOnlyList<string> Import(IReadOnlyList<CatalogDocument> entries)
    {
        var warnings = new List<string>();
        foreach (var entry in entries)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? Read(string key) => entry.Properties.TryGetValue(key, out var raw) &&
                !string.IsNullOrWhiteSpace(raw?.ToString()) ? raw.ToString()!.Trim() : null;
            string? Date(string key)
            {
                var raw = Read(key);
                if (raw is null || LoanDocument.TryReadDate(raw, out _))
                {
                    return raw;
                }

                if (Read(key + "Normalized") is { } normalized && LoanDocument.TryReadDate(normalized, out _))
                {
                    return normalized;
                }

                keys.Add(key);
                return null;
            }

            var borrower = Read("Borrower");
            var loanDate = Date("LoanDate");
            var lendDate = Date("LendDate");
            if (loanDate is not null && lendDate is not null && loanDate != lendDate)
            {
                keys.Add("LendDate");
            }

            var dueDate = Date("DueDate");
            var sourceId = Read("LoanUid");
            if (entry.Properties.ContainsKey("TellicoUnmappedLoans"))
            {
                keys.Add("TellicoUnmappedLoans");
            }

            var history = Read("Borrowings");
            if (borrower is null && history is null && loanDate is null && lendDate is null &&
                dueDate is null && sourceId is null && keys.Count == 0)
            {
                continue;
            }

            var existing = service.GetHistory(entry.Id).ToDictionary(loan => loan.Id, StringComparer.Ordinal);
            void Save(string name, string? loaned, string? due, string? returned, string? uid, string? rawHistoryId = null)
            {
                var key = System.Text.Json.JsonSerializer.Serialize(new[] { OperatingSystem.IsWindows() ? service.CatalogPath.ToUpperInvariant() : service.CatalogPath, entry.Id, uid, rawHistoryId, name, loaned, due, returned });
                var id = "import-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
                if (existing.ContainsKey(id))
                {
                    return;
                }

                var contact = service.FindOrCreateBorrower(name);
                var loan = new LoanDocument
                {
                    Id = id,
                    CatalogPath = service.CatalogPath,
                    EntryId = entry.Id,
                    BorrowerId = contact.Id,
                    LoanDate = loaned,
                    DueDate = due,
                    ReturnDate = returned,
                    SourceId = uid
                };
                service.Save(loan);
                existing.Add(id, loan);
            }

            if (borrower is not null)
            {
                Save(borrower, loanDate ?? lendDate, dueDate, null, sourceId);
            }
            else
            {
                foreach (var key in new[] { "LoanDate", "LendDate", "DueDate", "LoanUid" })
                {
                    if (Read(key) is not null)
                    {
                        keys.Add(key);
                    }
                }
            }

            if (history is not null)
            {
                var fields = history.Split('|', StringSplitOptions.TrimEntries);
                var count = fields.Length;
                if (count % 3 == 1 && fields[^1].Length == 0)
                {
                    count--;
                }

                for (var i = 0; i + 2 < count; i += 3)
                {
                    var name = fields[i];
                    var lent = fields[i + 1].Length == 0 ? null : fields[i + 1];
                    var returned = fields[i + 2].Length == 0 ? null : fields[i + 2];
                    if (name.Length == 0 || (lent is not null && !LoanDocument.TryReadDate(lent, out _)) ||
                        (returned is not null && !LoanDocument.TryReadDate(returned, out _)))
                    {
                        keys.Add("Borrowings");
                        continue;
                    }

                    if (returned is null && borrower is not null &&
                        string.Equals(name, borrower, StringComparison.OrdinalIgnoreCase) && lent == (loanDate ?? lendDate))
                    {
                        continue;
                    }

                    Save(name, lent, null, returned, null, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                if (count % 3 != 0)
                {
                    keys.Add("Borrowings");
                }
            }

            if (keys.Count > 0)
            {
                warnings.Add($"{entry.Name} ({string.Join(", ", keys.Order(StringComparer.OrdinalIgnoreCase))})");
            }
        }

        return warnings;
    }
}
