// Reference M source for the completed PBIP.
// Canonical typed queries live in OlistEcommerce.SemanticModel/definition/tables/*.tmdl.
// These files are produced by etl/export_powerbi.ps1 from Portfolio_Olist core/rpt objects.

section OlistPortfolio;

shared DataRoot = "C:\Portfolio\OlistEcommerce\data\processed\powerbi";

shared ReadSqlExport = (fileName as text, columnCount as number) as table =>
    Csv.Document(
        File.Contents(DataRoot & "\" & fileName),
        [Delimiter = "#(tab)", Columns = columnCount, Encoding = 65001, QuoteStyle = QuoteStyle.None]
    );

// Example. Each semantic-model table renames and explicitly types these columns.
shared DateExtract = ReadSqlExport("date.tsv", 13);
