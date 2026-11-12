using System.Windows.Controls;
namespace GraficaCaixa.Wpf;
public static class CustomerAutocomplete
{
    public static IReadOnlyList<CustomerProfile> Find(IEnumerable<CustomerProfile> customers,string query)
    {
        var words=ProductOperations.Normalize(query).Split(' ',StringSplitOptions.RemoveEmptyEntries);
        var digits=PhoneText.Digits(query);
        if(words.Length==0)return [];
        return customers.Where(c=>words.All(w=>ProductOperations.Normalize(c.Name).Contains(w))||(digits.Length>=2&&PhoneText.Matches(c.Contact,query)))
            .OrderBy(c=>ProductOperations.Normalize(c.Name).StartsWith(ProductOperations.Normalize(query))?0:1).ThenBy(c=>c.Name).Take(20).ToList();
    }
    public static void Attach(TextBox name,TextBox contact,Func<IReadOnlyList<CustomerProfile>> customers,Func<DataStore?>? remoteStore=null,Action<CustomerProfile>? selected=null)
    {
        void Select(SuggestionOption option){var customer=(CustomerProfile)option.Value;name.Text=customer.Name;contact.Text=CustomerDirectory.Clean(customer.Contact);selected?.Invoke(customer);}
        IReadOnlyList<SuggestionOption> Search(string query)=>Find(customers(),query).Select(c=>new SuggestionOption(c.Name,PhoneText.Format(CustomerDirectory.Clean(c.Contact)),c)).ToList();
        Func<string,CancellationToken,Task<IReadOnlyList<SuggestionOption>>> find;
        if(remoteStore==null){find=(query,_)=>Task.FromResult(Search(query));SuggestionPopup.Attach(name,"Clientes",Search,Select);SuggestionPopup.Attach(contact,"Clientes por telefone",Search,Select);}
        else {async Task<IReadOnlyList<SuggestionOption>> Remote(string query,CancellationToken ct)=>remoteStore()?.IsRemote==true?(await remoteStore()!.QueryRemoteAsync<List<CustomerProfile>>("desktop/lookup/customers?search="+Uri.EscapeDataString(query),ct)).Select(c=>new SuggestionOption(c.Name,PhoneText.Format(CustomerDirectory.Clean(c.Contact)),c)).ToList():Search(query);find=Remote;SuggestionPopup.AttachAsync(name,"Clientes",Remote,Select);SuggestionPopup.AttachAsync(contact,"Clientes por telefone",Remote,Select);}
        // Telefone completo de um único cliente e nome ainda vazio: preenche o nome ao sair do campo.
        contact.LostKeyboardFocus+=async(_,_)=>{
            if(!string.IsNullOrWhiteSpace(name.Text)||PhoneText.Digits(contact.Text).Length<8)return;
            try{var options=await find(contact.Text,CancellationToken.None);if(options.Count==1&&string.IsNullOrWhiteSpace(name.Text)){var customer=(CustomerProfile)options[0].Value;name.Text=customer.Name;selected?.Invoke(customer);}}
            catch{/* Sugestão é opcional: sem conexão, o usuário digita o nome. */}
        };
    }
}
