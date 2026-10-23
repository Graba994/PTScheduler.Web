namespace PTScheduler.Portal.Entities;

public enum InboxFolder { Inbox, Archived, Spam }

/// <summary>
/// Wątek w „Poczcie” Panelu: rozmowa z jedną osobą o jednym temacie (np. pytanie trenera wysłane na kontakt@).
/// Wiadomości przychodzą z Brevo (Inbound Parsing), odpowiedzi wychodzą przez SMTP platformy.
/// </summary>
public class InboxThread
{
    public int Id { get; set; }
    public string Subject { get; set; } = string.Empty;
    /// <summary>Temat bez „Re:”, „Odp:”, „Fwd:” — do łączenia odpowiedzi w wątek.</summary>
    public string SubjectKey { get; set; } = string.Empty;
    /// <summary>Adres osoby, z którą rozmawiamy (małe litery).</summary>
    public string Counterpart { get; set; } = string.Empty;
    public string? CounterpartName { get; set; }
    /// <summary>Trener rozpoznany po adresie e-mail właściciela instancji.</summary>
    public int? TenantId { get; set; }
    public Tenant? Tenant { get; set; }
    public InboxFolder Folder { get; set; } = InboxFolder.Inbox;
    public int Unread { get; set; }
    public DateTime LastAt { get; set; } = DateTime.UtcNow;
    /// <summary>Początek ostatniej wiadomości — podgląd na liście.</summary>
    public string? Snippet { get; set; }
    public List<InboxMessage> Messages { get; set; } = [];
}

public class InboxMessage
{
    public int Id { get; set; }
    public int ThreadId { get; set; }
    public InboxThread? Thread { get; set; }
    /// <summary>true — przyszła do nas; false — nasza odpowiedź z Panelu.</summary>
    public bool Incoming { get; set; }
    public string? MessageId { get; set; }
    public string? InReplyTo { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string? FromName { get; set; }
    public string ToAddress { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    /// <summary>Treść bez cytowanej historii (od Brevo) albo zwykły tekst.</summary>
    public string? Text { get; set; }
    /// <summary>Oryginalny HTML — pokazywany tylko w odizolowanej ramce, bez skryptów i obrazków z sieci.</summary>
    public string? Html { get; set; }
    public double? SpamScore { get; set; }
    /// <summary>Załączniki z Brevo: JSON [{name, type, size, token}] — pobieranie przez API Brevo.</summary>
    public string? AttachmentsJson { get; set; }
    /// <summary>Kto z administratorów odpowiedział (dla wiadomości wychodzących).</summary>
    public string? SentBy { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}
