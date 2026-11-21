using System.Xml;
using Iso20022.Pix.Components;

namespace Iso20022.Pix.Internal;

/// <summary>
/// Reading and writing of the shared components, in the element order of the
/// ISO 20022 schemas and with the same omission rules as the Go structs
/// (<c>omitempty</c> strings and optional components are left out when empty).
/// </summary>
internal static class Xsd
{
    public static void WriteChoice(XmlOutput w, string name, CodeOrProprietary? c)
    {
        if (c is null)
        {
            return;
        }

        w.Start(name);
        w.Optional("Cd", c.Cd);
        w.Optional("Prtry", c.Prtry);
        w.End(name);
    }

    public static void ReadChoice(XmlReader r, CodeOrProprietary c)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "Cd": c.Cd = XmlInput.ReadText(r); break;
                case "Prtry": c.Prtry = XmlInput.ReadText(r); break;
                default: r.Skip(); break;
            }
        }
    }

    public static CodeOrProprietary ReadChoiceInto(XmlReader r, CodeOrProprietary? existing)
    {
        CodeOrProprietary c = existing ?? new CodeOrProprietary();
        ReadChoice(r, c);
        return c;
    }

    public static void WriteParty(XmlOutput w, string name, Party p)
    {
        w.Start(name);
        w.Optional("Nm", p.Nm);
        if (p.Id is not null)
        {
            w.Start("Id");
            WriteOtherIds(w, "OrgId", p.Id.OrgId);
            WriteOtherIds(w, "PrvtId", p.Id.PrvtId);
            w.End("Id");
        }

        w.End(name);
    }

    public static void ReadParty(XmlReader r, Party p)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "Nm":
                    p.Nm = XmlInput.ReadText(r);
                    break;
                case "Id":
                    p.Id ??= new PartyId();
                    ReadPartyId(r, p.Id);
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
    }

    public static void WriteAccount(XmlOutput w, string name, Account a)
    {
        w.Start(name);
        AccountId id = a.Id ?? new AccountId();
        w.Start("Id");
        w.Optional("IBAN", id.IBAN);
        if (id.Othr is not null)
        {
            w.Start("Othr");
            w.Element("Id", id.Othr.Id);
            WriteChoice(w, "SchmeNm", id.Othr.SchmeNm);
            w.Optional("Issr", id.Othr.Issr);
            w.End("Othr");
        }

        w.End("Id");
        WriteChoice(w, "Tp", a.Tp);
        w.Optional("Ccy", a.Ccy);
        w.Optional("Nm", a.Nm);
        if (a.Prxy is not null)
        {
            w.Start("Prxy");
            WriteChoice(w, "Tp", a.Prxy.Tp);
            w.Element("Id", a.Prxy.Id);
            w.End("Prxy");
        }

        w.End(name);
    }

    public static void ReadAccount(XmlReader r, Account a)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "Id":
                    ReadAccountId(r, a.Id);
                    break;
                case "Tp":
                    a.Tp = ReadChoiceInto(r, a.Tp);
                    break;
                case "Ccy":
                    a.Ccy = XmlInput.ReadText(r);
                    break;
                case "Nm":
                    a.Nm = XmlInput.ReadText(r);
                    break;
                case "Prxy":
                    a.Prxy ??= new Proxy();
                    ReadProxy(r, a.Prxy);
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
    }

    public static void WriteAgent(XmlOutput w, string name, Agent a)
    {
        w.Start(name);
        w.Start("FinInstnId");
        ClearingSystemMemberId? m = a.FinInstnId?.ClrSysMmbId;
        if (m is not null)
        {
            w.Start("ClrSysMmbId");
            WriteChoice(w, "ClrSysId", m.ClrSysId);
            w.Element("MmbId", m.MmbId);
            w.End("ClrSysMmbId");
        }

        w.End("FinInstnId");
        w.End(name);
    }

    public static void ReadAgent(XmlReader r, Agent a)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            if (r.LocalName == "FinInstnId")
            {
                ReadFinancialInstitution(r, a.FinInstnId);
            }
            else
            {
                r.Skip();
            }
        }
    }

    public static void WriteRemittance(XmlOutput w, string name, RemittanceInformation? rmt)
    {
        if (rmt is null)
        {
            return;
        }

        w.Start(name);
        w.Elements("Ustrd", rmt.Ustrd);
        w.End(name);
    }

    public static RemittanceInformation ReadRemittance(XmlReader r, RemittanceInformation? existing)
    {
        RemittanceInformation rmt = existing ?? new RemittanceInformation();
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            if (r.LocalName == "Ustrd")
            {
                rmt.Ustrd.Add(XmlInput.ReadText(r));
            }
            else
            {
                r.Skip();
            }
        }

        return rmt;
    }

    public static void WriteDateAndDateTime(XmlOutput w, string name, DateAndDateTime? d)
    {
        if (d is null)
        {
            return;
        }

        w.Start(name);
        w.Optional("Dt", d.Dt);
        w.Optional("DtTm", d.DtTm);
        w.End(name);
    }

    public static DateAndDateTime ReadDateAndDateTime(XmlReader r, DateAndDateTime? existing)
    {
        DateAndDateTime d = existing ?? new DateAndDateTime();
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "Dt": d.Dt = XmlInput.ReadText(r); break;
                case "DtTm": d.DtTm = XmlInput.ReadText(r); break;
                default: r.Skip(); break;
            }
        }

        return d;
    }

    public static CurrencyAmount ReadAmountInto(XmlReader r, CurrencyAmount? existing)
    {
        CurrencyAmount a = existing ?? new CurrencyAmount();
        XmlInput.ReadAmount(r, a);
        return a;
    }

    private static void WriteOtherIds(XmlOutput w, string name, OtherIds? o)
    {
        if (o is null)
        {
            return;
        }

        w.Start(name);
        foreach (GenericId g in o.Othr)
        {
            w.Start("Othr");
            w.Element("Id", g.Id);
            WriteChoice(w, "SchmeNm", g.SchmeNm);
            w.Optional("Issr", g.Issr);
            w.End("Othr");
        }

        w.End(name);
    }

    private static void ReadPartyId(XmlReader r, PartyId id)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "OrgId":
                    id.OrgId ??= new OtherIds();
                    ReadOtherIds(r, id.OrgId);
                    break;
                case "PrvtId":
                    id.PrvtId ??= new OtherIds();
                    ReadOtherIds(r, id.PrvtId);
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
    }

    private static void ReadOtherIds(XmlReader r, OtherIds o)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            if (r.LocalName != "Othr")
            {
                r.Skip();
                continue;
            }

            var g = new GenericId();
            int d2 = XmlInput.Enter(r);
            while (XmlInput.NextChild(r, d2))
            {
                switch (r.LocalName)
                {
                    case "Id": g.Id = XmlInput.ReadText(r); break;
                    case "SchmeNm": g.SchmeNm = ReadChoiceInto(r, g.SchmeNm); break;
                    case "Issr": g.Issr = XmlInput.ReadText(r); break;
                    default: r.Skip(); break;
                }
            }

            o.Othr.Add(g);
        }
    }

    private static void ReadAccountId(XmlReader r, AccountId id)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "IBAN":
                    id.IBAN = XmlInput.ReadText(r);
                    break;
                case "Othr":
                    id.Othr ??= new GenericAccountId();
                    ReadGenericAccountId(r, id.Othr);
                    break;
                default:
                    r.Skip();
                    break;
            }
        }
    }

    private static void ReadGenericAccountId(XmlReader r, GenericAccountId o)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "Id": o.Id = XmlInput.ReadText(r); break;
                case "SchmeNm": o.SchmeNm = ReadChoiceInto(r, o.SchmeNm); break;
                case "Issr": o.Issr = XmlInput.ReadText(r); break;
                default: r.Skip(); break;
            }
        }
    }

    private static void ReadProxy(XmlReader r, Proxy p)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            switch (r.LocalName)
            {
                case "Tp": p.Tp = ReadChoiceInto(r, p.Tp); break;
                case "Id": p.Id = XmlInput.ReadText(r); break;
                default: r.Skip(); break;
            }
        }
    }

    private static void ReadFinancialInstitution(XmlReader r, FinancialInstitutionId f)
    {
        int depth = XmlInput.Enter(r);
        while (XmlInput.NextChild(r, depth))
        {
            if (r.LocalName != "ClrSysMmbId")
            {
                r.Skip();
                continue;
            }

            f.ClrSysMmbId ??= new ClearingSystemMemberId();
            ClearingSystemMemberId m = f.ClrSysMmbId;
            int d2 = XmlInput.Enter(r);
            while (XmlInput.NextChild(r, d2))
            {
                switch (r.LocalName)
                {
                    case "ClrSysId": m.ClrSysId = ReadChoiceInto(r, m.ClrSysId); break;
                    case "MmbId": m.MmbId = XmlInput.ReadText(r); break;
                    default: r.Skip(); break;
                }
            }
        }
    }
}
