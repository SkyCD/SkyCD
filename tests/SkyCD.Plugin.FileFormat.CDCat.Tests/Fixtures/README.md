# CdCat Test Fixtures

These fixtures are synthetic test data generated specifically for SkyCD's CdCat HCF format import test suite.

## License
MIT License. All fixture files in this directory are synthetic and redistributable under the repository's MIT license.

## Included Fixtures
- `cdcat-sample-v2.1.hcf`: Standard v2.1 catalog with multiple media, nested and empty directories, MP3 ID3 tags, video/audio comment metadata, borrow information, and archive content.
- `cdcat-unicode.hcf`: Catalog testing UTF-8 handling (Japanese, Cyrillic, accented Latin characters).
- `cdcat-uncompressed.xml`: Uncompressed XML format.
- `cdcat-unsupported-version-3.0.hcf`: Version 3.0 catalog (used to verify version rejection).
- `cdcat-unsupported-version-2.2.hcf`: Version 2.2 catalog (used to verify version rejection).
- `cdcat-empty.hcf`: Valid catalog structure with zero media entries.
- `cdcat-malformed-xml.hcf`: Valid gzip containing invalid XML.
- `cdcat-malformed-gzip.hcf`: Corrupt gzip byte stream.
