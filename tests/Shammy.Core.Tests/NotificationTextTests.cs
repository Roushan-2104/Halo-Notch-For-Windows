using Shammy.Core.Notifications;

namespace Shammy.Core.Tests;

public class NotificationTextTests
{
    [Fact]
    public void List_rows_show_title_app_and_body()
    {
        Assert.Equal(("Riya", "WhatsApp · see you at 6"), NotificationText.ForList("WhatsApp", "Riya", "see you at 6", presenting: false));
    }

    [Fact]
    public void List_rows_hide_title_and_body_while_private()
    {
        var (title, detail) = NotificationText.ForList("WhatsApp", "Riya", "the door code is 4471", presenting: true);
        Assert.Equal("WhatsApp", title);
        Assert.Equal(NotificationText.ListHidden, detail);
        Assert.DoesNotContain("Riya", title + detail);
        Assert.DoesNotContain("4471", title + detail);
    }

    [Fact]
    public void List_rows_flatten_multi_line_bodies_and_skip_empty_ones()
    {
        Assert.Equal(("Mail", "Outlook · line one line two"), NotificationText.ForList("Outlook", "Mail", "line one\n\n line two ", false));
        Assert.Equal(("Done", "Downloads"), NotificationText.ForList("Downloads", "Done", "", false));
    }

    [Fact]
    public void Popups_keep_their_text_unless_private()
    {
        Assert.Equal(("Riya", "hi"), NotificationText.ForPopup("WhatsApp", "Riya", "hi", false));
        Assert.Equal(("WhatsApp", NotificationText.PopupHidden), NotificationText.ForPopup("WhatsApp", "Riya", "hi", true));
    }
}
