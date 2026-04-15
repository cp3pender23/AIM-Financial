namespace AIM.IngestCsv;

/// <summary>
/// POCO that CsvHelper maps CSV columns into.
/// All fields nullable so CsvHelper never throws on missing columns.
/// </summary>
public class VendorRow
{
    public int?     VendorId          { get; set; }
    public string?  VendorName        { get; set; }
    public string?  ProductName       { get; set; }
    public string?  StreetName        { get; set; }
    public string?  City              { get; set; }
    public string?  State             { get; set; }
    public string?  ZipCode           { get; set; }
    public string?  SellerFirstName   { get; set; }
    public string?  SellerLastName    { get; set; }
    public string?  SellerPhone       { get; set; }
    public string?  SellerEmail       { get; set; }
    public string?  SellerUrl         { get; set; }
    public bool?    SellerNameChange  { get; set; }
    public bool?    ArticleFinding    { get; set; }
    public string?  ArticleUrl        { get; set; }
    public string?  ProductCategory   { get; set; }
    public decimal? AnnualSales       { get; set; }
    public bool?    VerifiedCompany   { get; set; }
    public decimal? PriceDifference   { get; set; }
    public decimal? ProductPrice      { get; set; }
    public bool?    DifferentAddress  { get; set; }
    public decimal? Weight            { get; set; }
}
