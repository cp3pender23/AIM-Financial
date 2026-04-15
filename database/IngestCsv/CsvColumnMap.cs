using CsvHelper.Configuration;

namespace AIM.IngestCsv;

/// <summary>
/// Maps all known CSV header name variants to VendorRow properties.
/// Add new Name() entries here when a new data source uses different column names.
/// The PrepareHeaderForMatch lambda in Program.cs normalizes headers to
/// lowercase_with_underscores before matching, so most common variants are
/// handled automatically. These Name() entries are a fallback for unusual casing.
/// </summary>
public sealed class VendorRowMap : ClassMap<VendorRow>
{
    public VendorRowMap()
    {
        Map(m => m.VendorId).Name(
            "vendor_id", "VENDOR_ID", "VendorId", "Vendor ID", "vendor id");

        Map(m => m.VendorName).Name(
            "vendor_name", "VENDOR_NAME", "VendorName", "Vendor Name", "vendor name", "Company");

        Map(m => m.ProductName).Name(
            "product_name", "PRODUCT_NAME", "ProductName", "Product Name", "product name", "Item");

        Map(m => m.StreetName).Name(
            "street_name", "STREET_NAME", "StreetName", "Street Name", "Street", "Address");

        Map(m => m.City).Name(
            "city", "CITY", "City");

        Map(m => m.State).Name(
            "state", "STATE", "State");

        Map(m => m.ZipCode).Name(
            "zip_code", "ZIP_CODE", "ZipCode", "Zip Code", "Zip", "Postal Code");

        Map(m => m.SellerFirstName).Name(
            "seller_first_name", "SELLER_FIRST_NAME", "SellerFirstName", "First Name", "Seller First Name");

        Map(m => m.SellerLastName).Name(
            "seller_last_name", "SELLER_LAST_NAME", "SellerLastName", "Last Name", "Seller Last Name");

        Map(m => m.SellerPhone).Name(
            "seller_phone", "SELLER_PHONE", "SellerPhone", "Phone", "Seller Phone");

        Map(m => m.SellerEmail).Name(
            "seller_email", "SELLER_EMAIL", "SellerEmail", "Email", "Seller Email");

        Map(m => m.SellerUrl).Name(
            "seller_url", "SELLER_URL", "SellerUrl", "URL", "Website", "Seller URL");

        Map(m => m.SellerNameChange).Name(
            "seller_name_change", "SELLER_NAME_CHANGE", "SellerNameChange");

        Map(m => m.ArticleFinding).Name(
            "article_finding", "ARTICLE_FINDING", "ArticleFinding");

        Map(m => m.ArticleUrl).Name(
            "article_url", "ARTICLE_URL", "ArticleUrl", "Article URL");

        // Accept both the corrected spelling and the original typo from the MySQL source
        Map(m => m.ProductCategory).Name(
            "product_category", "PRODUCT_CATEGORY", "ProductCategory",
            "product_gategory", "PRODUCT_GATEGORY",   // original MySQL typo
            "Category");

        Map(m => m.AnnualSales).Name(
            "annual_sales", "ANNUAL_SALES", "AnnualSales", "Annual Sales");

        Map(m => m.VerifiedCompany).Name(
            "verified_company", "VERIFIED_COMPANY", "VerifiedCompany", "Verified");

        // Accept both the corrected spelling and the original typo
        Map(m => m.PriceDifference).Name(
            "price_difference", "PRICE_DIFFERENCE", "PriceDifference",
            "price_differance", "PRICE_DIFFERANCE",   // original MySQL typo
            "Price Difference");

        Map(m => m.ProductPrice).Name(
            "product_price", "PRODUCT_PRICE", "ProductPrice", "Product Price", "Price");

        // Accept both the corrected spelling and the original typo
        Map(m => m.DifferentAddress).Name(
            "different_address", "DIFFERENT_ADDRESS", "DifferentAddress",
            "diffrent_address", "DIFFRENT_ADDRESS",   // original MySQL typo
            "Different Address");

        Map(m => m.Weight).Name(
            "weight", "WEIGHT", "Weight");
    }
}
