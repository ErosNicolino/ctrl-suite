using System;

namespace GraficaCaixa;

public static class CounterCustomer
{
	public const string Name = "Cliente Balcão";

	public static string Reference(string phoneOrSuffix)
	{
		string text = PhoneText.Digits(phoneOrSuffix);
		string text2;
		if (text.Length > 4)
		{
			text2 = text.Substring(text.Length - 4);
		}
		else
		{
			text2 = text;
		}
		string text3 = text2;
		if (!string.IsNullOrWhiteSpace(text3))
		{
			return "Cliente Balcão • WhatsApp final " + text3;
		}
		return "Cliente Balcão";
	}

	public static bool Matches(string phone, string query)
	{
		string text = PhoneText.Digits(phone);
		string text2 = PhoneText.Digits(query);
		if (text2.Length > 0)
		{
			return text.EndsWith(text2, StringComparison.Ordinal);
		}
		return false;
	}
}
