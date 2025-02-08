using BlogEngine.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Web.Hosting;
using System.Xml;

namespace BlogEngine.NET.Custom.Widgets
{
    public static class NodeExtension
    {
        public static XmlDocument SetAttribute(this XmlDocument doc, XmlNode node, string name, string value = "false")
        {
            var item = doc.CreateAttribute(name);
            item.Value = value;
            node.Attributes.SetNamedItem(item);
            return doc;
        }
    }

    public class Newsletter
    {
        private static readonly object syncRoot = new object();

        
        /// <summary>
        /// Add subscriber emal
        /// </summary>
        /// <param name="email">Email address</param>
        public static void AddEmail(string email, object sender=null)
        {
            var doc = GetXml();
            XmlNode id = null;
            XmlNode blocked = null;
            XmlNode node = doc.SelectSingleNode($"emails/email[text()='{email}']");

            if (node == null)
            {
                node = doc.CreateElement("email");
                node.InnerText = email;

                doc
                    .SetAttribute(node, "confirmed")
                    .SetAttribute(node, "id", Guid.NewGuid().ToString())
                    .SetAttribute(node, "blocked")
                    .FirstChild.AppendChild(node);
                
                id = node.Attributes["id"];
            } 
            else
            {
                XmlNode confirmed = node.Attributes["confirmed"];
                if (confirmed == null)
                    doc.SetAttribute(node, "confirmed");

                id = node.Attributes["id"];
                if (id == null)
                    doc.SetAttribute(node, "id", Guid.NewGuid().ToString());

                blocked = node.Attributes["blocked"];
                if (blocked == null)
                    doc.SetAttribute(node, "blocked");
                
            }

            //Utils.Log($"Newsletter.AddEmai.SaveEmails()");
            SaveEmails(doc);

            //Utils.Log($"Newsletter.AddEmai.CreateVerificationEmail");
            var message = CreateVerificationEmail(email, id.Value, sender);
            try
            {
                //Utils.Log($"Newsletter.AddEmai.SendMailMessage()");
                var results = Utils.SendMailMessage(message);
                if(!results.Contains("Error"))
                    Utils.Log("sent to " + email + " - " + message.Subject);
            }
            catch (Exception ex)
            {
                Utils.Log("Custom.Widgets.Newsletter.SendEmails", ex);
            }
            //Utils.Log($"Newsletter.AddEmai - COMPLETED");
        }

        public static void EmailVerified(string uniqueId, string email)
        {
            var doc = GetXml();
            XmlNode node = doc.SelectSingleNode($"emails/email[text()='{email}']");

            XmlNode confirmed = node.Attributes["confirmed"];
            XmlNode id = node.Attributes["id"];
            var idIsvalid = id.Value == uniqueId;

            if (confirmed == null)
            {
                confirmed = doc.CreateAttribute("confirmed");
            }
            confirmed.Value = $"{idIsvalid}";
            node.Attributes.SetNamedItem(confirmed);

            SaveEmails(doc);
        }

        public static void RemoveEmail(string uniqueId, string email)
        {
            var doc = GetXml();
            XmlNode node = doc.SelectSingleNode($"emails/email[text()='{email}']");

            XmlNode id = node.Attributes["id"];
            var idIsvalid = id.Value == uniqueId;

            if (idIsvalid)
                RemoveEmail(email);
        }

        /// <summary>
        /// Remove email from subscription
        /// </summary>
        /// <param name="email">Email address</param>
        public static void RemoveEmail(string email)
        {
            var doc = GetXml();
            var node = doc.SelectSingleNode($"emails/email[text()='{email}']");
            if (node != null)
            {
                // doc.FirstChild.RemoveChild(node);
                var blocked = (node.Attributes["blocked"]?.Value ?? "False") == "True";

                doc.SetAttribute(node, "blocked", blocked ? "False" : "True"); // Toggle
                SaveEmails(doc);
            }
        }

        /// <summary>
        /// Load emails
        /// </summary>
        /// <returns>List of emails</returns>
        public static List<XmlNode> LoadEmails()
        {
            var emailList = new List<XmlNode>();
            var doc = GetXml();
            var emails = doc.SelectNodes("emails/email");
            if (emails != null)
            {
                foreach (XmlNode node in emails)
                {
                    if (node.Attributes.Count > 0)
                    {
                        var confirmed = node.Attributes["confirmed"]?.Value;
                        if (confirmed == null) continue;
                        //if (!bool.Parse(confirmed)) continue;
                    }
                    emailList.Add(node);
                }
            }
            return emailList.OrderBy(e => e.Attributes["confirmed"].Value).ToList();
        }

        /// <summary>
        /// Send emails to newsletter subscribers
        /// </summary>
        public static void SendEmails(IPublishable publishable)
        {
            var emails = LoadEmails();
            if (emails != null && emails.Count > 0)
            {
                foreach (var emailNode in emails)
                {
                    var email = emailNode.InnerText.Trim();
                    var id = emailNode.Attributes["id"]?.Value;
                    var blocked = (emailNode.Attributes["blocked"]?.Value ?? "False") == "True";
                    var confirmed = (emailNode.Attributes["confirmed"]?.Value ?? "False") == "True";

                    if (!String.IsNullOrWhiteSpace(email) && Utils.IsEmailValid(email) && !blocked && confirmed)
                    {
                        MailMessage message = CreateEmail(publishable, email, id);
                        message.To.Add(email);
                        try
                        {
                            Utils.SendMailMessage(message);
                            Utils.Log("sent to " + email + " - " + publishable.Title);
                        }
                        catch (Exception ex)
                        {
                            Utils.Log("Custom.Widgets.Newsletter.SendEmails", ex);
                        }
                    }
                }
            }
        }

        #region Private methods

        private static XmlDocument GetXml()
        {
            var filename = HostingEnvironment.MapPath(
                Path.Combine(Blog.CurrentInstance.StorageLocation, "newsletter.xml"));

            var doc = new XmlDocument();
            try
            {
                lock (syncRoot)
                {
                    if (File.Exists(filename))
                    {
                        doc.Load(filename);
                        var xmlDoc = doc.OuterXml; // RAW XML
                    }
                    else
                    {
                        doc.LoadXml("<emails></emails>");
                    }
                }
            }
            catch (Exception ex)
            {
                Utils.Log("Custom.Widgets.Newsletter.GetXml", ex);
            }
            return doc;
        }

        private static void SaveEmails(XmlDocument doc)
        {
            var filename = HostingEnvironment.MapPath(
                Path.Combine(Blog.CurrentInstance.StorageLocation, "newsletter.xml"));

            lock (syncRoot)
            {
                using (var ms = new MemoryStream())
                using (var fs = File.Open(filename, FileMode.Create, FileAccess.Write))
                {
                    doc.Save(ms);
                    ms.WriteTo(fs);
                }
            }
        }

        private static string FormatBodyMail(IPublishable publishable, string email, string uniqueid)
        {
            var body = new StringBuilder();
            var removeLink = "removeLink";
            var removeLinkDesc = "removeLinkDesc";
            var bodyInfo = "";

            var urlbase = Path.Combine(
                Path.Combine(Utils.AbsoluteWebRoot.AbsoluteUri, "themes"), BlogSettings.Instance.Theme);

            var filePath = $"~/Custom/Themes/{BlogSettings.Instance.Theme}/newsletter.html";

            removeLink = $"{Utils.AbsoluteWebRoot.AbsoluteUri}contact.aspx?id={uniqueid}&email={email}&mode=remove";
            removeLinkDesc = "Click to unsubscribe and terminate notifications";

            filePath = HostingEnvironment.MapPath(filePath);
            if (File.Exists(filePath))
            {
                body.Append(File.ReadAllText(filePath));
            }
            else
            {
                // if custom theme doesn't have email template
                // use email template from standard theme
                filePath = HostingEnvironment.MapPath("~/Custom/Themes/Standard/newsletter.html");
                if (File.Exists(filePath))
                {
                    body.Append(File.ReadAllText(filePath));
                }
                else
                {
                    Utils.Log(
                        "When sending newsletter, newsletter.html does not exist " +
                        "in theme folder, and does not exist in the Standard theme " +
                        "folder.");
                }
            }
            var linkDescription = $"Click to view {BlogSettings.Instance.EmailSubjectPrefix} - {publishable.Title}";
            body = body.Replace("[TITLE]", publishable.Title);
            body = body.Replace("[LINK]", publishable.AbsoluteLink.AbsoluteUri);
            body = body.Replace("[LINK_DESCRIPTION]", linkDescription);
            body = body.Replace("[BODY_INFO]", bodyInfo);
            body = body.Replace("[REMOVE_LINK]", removeLink);
            body = body.Replace("[REMOVE_LINK_DESC]", removeLinkDesc);
            body = body.Replace("[WebRoot]", Utils.AbsoluteWebRoot.AbsoluteUri);
            body = body.Replace("[httpBase]", urlbase);
            return body.ToString();
        }

        private static MailMessage CreateEmail(IPublishable publishable, string email, string uniqueId)
        {
            var subject = $"{publishable.Title}";

            var mail = new MailMessage
            {
                Subject = subject,
                Body = FormatBodyMail(publishable, email, uniqueId),
                From = new MailAddress(BlogSettings.Instance.Email, BlogSettings.Instance.Name)
            };
            return mail;
        }

        private static string FormatVerificationBodyMail(object sender, string uniqueid, string email)
        {
            var body = new StringBuilder();
            var title = "title";
            var link = "link";
            var description = "description";
            var removeLink = "removeLink";
            var removeLinkDesc = "removeLinkDesc";
            var bodyInfo = "<p>If you did not request to be notified please ignore this email - no emails will be sent</p>";

            var urlbase = Path.Combine(
                Path.Combine(Utils.AbsoluteWebRoot.AbsoluteUri, "themes"), BlogSettings.Instance.Theme);

            link = $"{Utils.AbsoluteWebRoot.AbsoluteUri}contact.aspx?id={uniqueid}&email={email}&mode=verify";
            title = $"Verification email from {BlogSettings.Instance.EmailSubjectPrefix}";

            removeLink = $"{Utils.AbsoluteWebRoot.AbsoluteUri}";
            removeLinkDesc = "Click to view website";

            description = "Click to accept notifications";

            var filePath = $"~/Custom/Themes/{BlogSettings.Instance.Theme}/newsletter.html";

            filePath = HostingEnvironment.MapPath(filePath);

            if (File.Exists(filePath))
            {
                body.Append(File.ReadAllText(filePath));
            }
            else
            {
                // if custom theme doesn't have email template
                // use email template from standard theme
                filePath = HostingEnvironment.MapPath("~/Custom/Themes/Standard/newsletter.html");
                if (File.Exists(filePath))
                {
                    body.Append(File.ReadAllText(filePath));
                }
                else
                {
                    Utils.Log(
                        "When sending newsletter, verification.html does not exist " +
                        "in theme folder, and does not exist in the Standard theme " +
                        "folder.");
                }
            }

            body = body.Replace("[TITLE]", title);
            body = body.Replace("[LINK]", link); // publishable.AbsoluteLink.AbsoluteUri);
            body = body.Replace("[LINK_DESCRIPTION]", description);
            body = body.Replace("[BODY_INFO]", bodyInfo);
            body = body.Replace("[REMOVE_LINK]", removeLink);
            body = body.Replace("[REMOVE_LINK_DESC]", removeLinkDesc);
            body = body.Replace("[WebRoot]", Utils.AbsoluteWebRoot.AbsoluteUri);
            body = body.Replace("[httpBase]", urlbase);
            return body.ToString();
        }

        private static MailMessage CreateVerificationEmail(string email, string uniqueId, object sender)
        {
            var subject = $"Verification email from {BlogSettings.Instance.EmailSubjectPrefix}";

            var mail = new MailMessage
            {
                Subject = subject,
                Body = FormatVerificationBodyMail(sender, uniqueId, email),
                From = new MailAddress(BlogSettings.Instance.Email, BlogSettings.Instance.Name)
            };
            mail.To.Add(email);
            return mail;
        }

        #endregion
    }
}