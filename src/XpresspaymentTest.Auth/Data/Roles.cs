namespace XpresspaymentTest.Auth.Data;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Support = "Support";
    public const string Customer = "Customer";

    public static readonly string[] All = [Admin, Support, Customer];
}

public static class Policies
{
    // Admin + Support staff, used for anything back-office
    public const string BackOffice = "BackOffice";
}
