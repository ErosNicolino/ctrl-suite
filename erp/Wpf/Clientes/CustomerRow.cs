using System;
using System.Collections.Generic;
using System.Linq;

namespace GraficaCaixa.Wpf;

public sealed class CustomerRow : System.ComponentModel.INotifyPropertyChanged
{
	public Guid Id { get; }
	// Tipo de preço (Balcão = "", Revenda, Empresa): mostrado e alterado na ficha do cliente.
	public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
	string priceType = "";
	public string PriceType { get => priceType; set { priceType = value ?? ""; PropertyChanged?.Invoke(this, new(nameof(PriceType))); PropertyChanged?.Invoke(this, new(nameof(PriceTypeVisibility))); } }
	public System.Windows.Visibility PriceTypeVisibility => priceType.Length > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

	public string Name { get; }
	// Mesma regra do filtro "Só incompletos": selo "Completar cadastro" na lista.
	public System.Windows.Visibility IncompleteVisibility=>GraficaCaixa.CustomerBasicOperations.IsIncomplete(new GraficaCaixa.CustomerProfile{Name=Name,Contact=Contact})?System.Windows.Visibility.Visible:System.Windows.Visibility.Collapsed;

	public string Contact { get; }

	public string Customer
	{
		get
		{
			if (!(Contact == "Não informado"))
			{
				return Name + " • " + Contact;
			}
			return Name;
		}
	}

	public int Orders { get; }
	// Iniciais para o avatar do cartão (UX 2.0).
	public string Initials { get { var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(x => char.IsLetterOrDigit(x[0])).Take(2).ToArray(); return parts.Length == 0 ? "?" : string.Concat(parts.Select(x => char.ToUpperInvariant(x[0]))); } }
    readonly bool deferred;public string OrdersDisplay=>deferred?"—":Orders.ToString();

	public string Total { get; }

	public string Pending { get; }

	public string LastOrder { get; }

	public string Details { get; }

	// Linha do modo conectado: totais vindos do servidor (null = sem permissão ou não informado).
	public CustomerRow(Guid id,string name,string contact,int? orders,decimal? received,decimal? pending,DateTime? last){Id=id;Name=name;Contact=PhoneText.Format(CustomerDirectory.Clean(contact));deferred=orders==null;Orders=orders??0;Total=received?.ToString("C2")??"—";Pending=pending?.ToString("C2")??"—";LastOrder=last?.ToString("dd/MM/yyyy")??"—";Details="";}
	public CustomerRow(string name, string contact, AppData d, Guid id = default(Guid),bool deferred=false)
	{
		Id = id;this.deferred=deferred;
		Name = name;
		Contact = PhoneText.Format(CustomerDirectory.Clean(contact));
		CustomerRecords owners = CustomerRecords.For(d);
		List<ScheduledOrder> list = d.ScheduledOrders.Where((ScheduledOrder x) => owners.Owner(x) == id).ToList();
		List<Sale> source = (from x in d.Sessions.SelectMany((CashSession x) => x.Sales)
			where owners.Owner(x) == id
			select x).ToList();
		Orders = list.Count;
		Total = source.Sum((Sale x) => x.Total).ToString("C2");
		Pending = list.Sum((ScheduledOrder x) => x.RemainingAmount).ToString("C2");
		if(deferred){Total=Pending="—";}
		LastOrder = list.OrderByDescending((ScheduledOrder x) => x.CreatedAt).FirstOrDefault()?.CreatedAt.ToString("dd/MM/yyyy") ?? "—";
		Details = string.Join("\n\n", from x in list
			orderby x.CreatedAt descending
			select $"Pedido #{x.Number} • {x.CreatedAt:dd/MM/yyyy}\n{x.ItemsDescription}\n{x.Stage} • saldo {x.RemainingAmount:C2}");
	}
}
