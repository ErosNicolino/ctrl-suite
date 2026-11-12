namespace GraficaCaixa;

public static class CustomerDirectory
{
    public static string Clean(string? value) => string.IsNullOrWhiteSpace(value) || new[]{"não informado","null","none"}.Contains(value.Trim().ToLowerInvariant()) ? "" : value.Trim();
    public static CustomerProfile? Resolve(AppData data,string name,string contact="")
    {
        var matches=data.Customers.Where(x=>x.Name.Equals(name,StringComparison.CurrentCultureIgnoreCase)).ToList();
        if(matches.Count==1)return matches[0];
        var exact=matches.Where(x=>Clean(x.Contact).Length>0&&PhoneText.Normalize(Clean(x.Contact))==PhoneText.Normalize(Clean(contact))).ToList();
        return exact.Count==1?exact[0]:null;
    }
    public static string Label(AppData? data,string name,string fallback="")
    {
        var customer=data==null?null:Resolve(data,name,fallback);
        var phone=PhoneText.Format(Clean(customer?.Contact??fallback));var title=Clean(customer?.Name??name);
        return phone.Length==0?title:$"{title} - {phone}";
    }
    public static void Edit(AppData data,CustomerProfile original,string name,string contact)
        =>CustomerBasicOperations.Edit(data,original,name,contact,GraficaCaixa.Wpf.App.CurrentUser);
}