using System;
using System.Collections.Generic;
using HtmlAgilityPack;
using ScrapySharp.Extensions;
using ScrapySharp.Network;
using System.IO;
using System.Globalization;
using CsvHelper;
using System.Text;
using System.Diagnostics;
using System.Formats.Asn1;
using System.Xml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.DependencyInjection;
using static Microsoft.FSharp.Core.ByRefKinds;
using System.Text.RegularExpressions;

namespace scraper
{
    class Program
    {
        static ScrapingBrowser _scrapingbrowser = new ScrapingBrowser();

        static void Main(string[] args)
        {
            using (var context = new WebDbContext())
            {
                var blacklistedCategories = context.BlacklistedCategoryNames.ToList();
                var blackCategories = new List<string>();
                var blacklistedPages = context.BlacklistedPages.ToList();
                var blackPages = new List<string>();
                var websites = context.Websites.ToList();
                Website website = websites[0];
                var existingCategories = context.Categories.ToList();
                foreach (var category in blacklistedCategories)
                {
                    blackCategories.Add(category.Name);
                }
                foreach(var page in blacklistedPages)
                {
                    blackPages.Add(page.Url);
                }
                var categories = GetCategories(website.Url, blackCategories, website);
                foreach(var category in categories)
                {
                    Category cat = new Category();
                    cat.Name = category;
                    cat.SourceUrl = website.Url;
                    if(!existingCategories.Contains(cat))
                        context.Categories.Add(cat);
                }
                context.SaveChanges();
                
                var eventLinks = new List<string>();
                var categoryList = context.Categories.ToList();
                var ExistingEvents = context.Events.ToList();
                List<string> EventUrls = new List<string>();
                foreach (var link in ExistingEvents)
                {
                    EventUrls.Add(link.Url);
                }
                foreach (var category in  categoryList)
                {
                    var smallCategory = category.Name.ToLower();
                    int pageAmount = GetPageAmount(website.Url + website.UrlExtensionForEvent + smallCategory, website);
                    if (pageAmount == 0)
                    {
                        eventLinks = GetEventLinks(website.Url + website.UrlExtensionForEvent + smallCategory, eventLinks, website, blackPages);
                    }
                    else
                    {
                        eventLinks = GetEventLinks(website.Url + website.UrlExtensionForEvent + smallCategory + "/page:1", eventLinks, website, blackPages);
                        //Pausing the core for 30 seconds after getting all links from one page
                        Thread.Sleep(30000);
                    }
                    var listEventDetails = GetEventDetails(eventLinks, website, category.Name, blackPages, EventUrls);
                    foreach (var eventLink in listEventDetails)
                    {
                        context.Events.Add(eventLink);
                    }
                    context.SaveChanges();
                    //Pausing the code for a minute in an attempt to avoid getting locked out of the page
                    Thread.Sleep(60000);
                }
            }
        }

        static List<string> GetEventLinks(string url, List<string> eventLinks, Website website, List<string> blacklistedPages)
        {
            var html = GetHtml(url);
            var links = html.CssSelect(website.EventLink);

            foreach (var link in links)
            {
                string UrlString = link.Attributes["href"].Value;
                if (!blacklistedPages.Contains(UrlString))
                    eventLinks.Add(UrlString);
            }
            return eventLinks;
        }
        static List<string> GetCategories(string url, List<string> blackCategories, Website website)
        {
            var html = GetHtml(url);
            List<string> categories = new List<string>();
            var categoryLinks = html.CssSelect(website.CategoryLink);
            foreach (var link in categoryLinks)
            {
                string result = link.InnerText;
                result = Regex.Replace(result, @"^\s+|\s+$", "");
                if (!categories.Contains(result) && !blackCategories.Contains(result))
                {
                    categories.Add(result);
                }
            }
            return categories;
        }
        static List<Event> GetEventDetails(List<string> urls, Website website, string CategoryName, List<string> blacklistedPages, List<string> EventUrls)
        {
            var listEventDetails = new List<Event>();

            foreach (var url in urls)
            {
                var htmlNode = GetHtml(url);
                var EventObject = new Event();
                if (htmlNode.OwnerDocument.DocumentNode.SelectSingleNode(website.LocationPath) == null)
                {
                    List<string> newEvents = new List<string>();
                    var eventLinks = GetEventLinks(url, newEvents, website, blacklistedPages);
                    foreach(var eventLink in eventLinks)
                    {
                        var newHtmlNode = GetHtml(eventLink);
                        var newEventObject = new Event();
                        newEventObject.Location = newHtmlNode.OwnerDocument.DocumentNode.SelectSingleNode(website.LocationPath).InnerText;
                        newEventObject.ImageLink = newHtmlNode.OwnerDocument.DocumentNode.SelectSingleNode(website.ImagePath).Attributes["src"].Value;
                        newEventObject.Url = eventLink;
                        string newTempTitle = newHtmlNode.OwnerDocument.DocumentNode.SelectSingleNode(website.TitlePath).InnerText;
                        newTempTitle = Regex.Replace(newTempTitle, @"^\s+|\s+$", "");
                        newTempTitle = Regex.Replace(newTempTitle, "&quot;", "\"");
                        newEventObject.Title = Regex.Replace(newTempTitle, "&#039;", "'");

                        newEventObject.Category = CategoryName;
                        newEventObject.Updated = false;
                        if (newHtmlNode.OwnerDocument.DocumentNode.SelectSingleNode(website.PricePath) == null)
                        {
                            newEventObject.Price = "Prekyba bilietais nebevykdoma";
                        }
                        else
                        {
                            newEventObject.Price = newHtmlNode.OwnerDocument.DocumentNode.SelectSingleNode(website.PricePath).InnerText;

                        }

                        string newTempDate = newHtmlNode.OwnerDocument.DocumentNode.SelectSingleNode(website.DatePath).InnerText;
                        newTempDate = Regex.Replace(newTempDate, "[a-zA-Z]+", "");
                        newTempDate = Regex.Replace(newTempDate, "&#32;", " ");
                        newTempDate = Regex.Replace(newTempDate, @"^\s+|\s+$", "");
                        newTempDate = Regex.Replace(newTempDate, "[ąčęėįšųūžĄČĘĖĮŠŲŪŽ]", "");
                        newTempDate = Regex.Replace(newTempDate, "  ", " ");

                        if (newTempDate.Contains(" - "))
                        {
                            string[] dates = Regex.Split(newTempDate, @"\s-\s");
                            newEventObject.DateStart = DateTime.Parse(dates[0]);
                            newEventObject.DateEnd = DateTime.Parse(dates[1]);

                        }
                        else
                        {
                            newEventObject.DateStart = DateTime.Parse(newTempDate);
                            newEventObject.DateEnd = DateTime.Parse(newTempDate);
                        }

                        if (newHtmlNode.OwnerDocument.DocumentNode.SelectSingleNode(website.TicketPath) == null)
                        {
                            newEventObject.TicketLink = "";
                        }
                        else
                        {
                            newEventObject.TicketLink = newHtmlNode.OwnerDocument.DocumentNode.SelectSingleNode(website.TicketPath).Attributes["data-shopurl"].Value;
                        }
                        if(newEventObject.Location != null && !EventUrls.Contains(newEventObject.Url))
                            listEventDetails.Add(newEventObject);
                    }
                }
                else
                {
                    EventObject.Location = htmlNode.OwnerDocument.DocumentNode.SelectSingleNode(website.LocationPath).InnerText;
                }
                EventObject.ImageLink = htmlNode.OwnerDocument.DocumentNode.SelectSingleNode(website.ImagePath).Attributes["src"].Value;
                EventObject.Url = url;
                string tempTitle = htmlNode.OwnerDocument.DocumentNode.SelectSingleNode(website.TitlePath).InnerText;
                tempTitle = Regex.Replace(tempTitle, @"^\s+|\s+$", "");
                tempTitle = Regex.Replace(tempTitle, "&quot;", "\"");
                EventObject.Title = Regex.Replace(tempTitle, "&#039;", "'");
                
                EventObject.Category = CategoryName;
                EventObject.Updated = false;
                if (htmlNode.OwnerDocument.DocumentNode.SelectSingleNode(website.PricePath) == null)
                {
                    EventObject.Price = "Prekyba bilietais nebevykdoma";
                }
                else
                {
                    EventObject.Price = htmlNode.OwnerDocument.DocumentNode.SelectSingleNode(website.PricePath).InnerText;

                }

                string tempDate = htmlNode.OwnerDocument.DocumentNode.SelectSingleNode(website.DatePath).InnerText;
                tempDate = Regex.Replace(tempDate, "[a-zA-Z]+", "");
                tempDate = Regex.Replace(tempDate, "&#32;", " ");
                tempDate = Regex.Replace(tempDate, @"^\s+|\s+$", "");
                tempDate = Regex.Replace(tempDate, "[ąčęėįšųūžĄČĘĖĮŠŲŪŽ]", "");
                tempDate = Regex.Replace(tempDate, "  ", " ");

                if(tempDate.Contains(" - "))
                {
                    string[] dates = Regex.Split(tempDate, @"\s-\s");
                    EventObject.DateStart = DateTime.Parse(dates[0]);
                    EventObject.DateEnd = DateTime.Parse(dates[1]);

                }
                else
                {
                    EventObject.DateStart = DateTime.Parse(tempDate);
                    EventObject.DateEnd = DateTime.Parse(tempDate);
                }

                if (htmlNode.OwnerDocument.DocumentNode.SelectSingleNode(website.TicketPath) == null)
                {
                    EventObject.TicketLink = "";
                }
                else
                {
                    EventObject.TicketLink = htmlNode.OwnerDocument.DocumentNode.SelectSingleNode(website.TicketPath).Attributes["data-shopurl"].Value;
                }
                if(EventObject.Location != null && EventUrls.Contains(EventObject.Url))
                    listEventDetails.Add(EventObject);
            }
            return listEventDetails;
        }

        static int GetPageAmount(string url, Website website)
        {
            var pages = new List<int>();
            var htmlNode = GetHtml(url);
            int maxPage = int.MinValue;
            int pageNumber = 0;

            var nodes = htmlNode.OwnerDocument.DocumentNode.SelectNodes(website.PagerLink);
            if (nodes == null)
                return 0;

            foreach (var node in nodes)
            {
                var page = node.InnerText;
                pageNumber = Int32.Parse(page);
                if (pageNumber > maxPage)
                {
                    maxPage = pageNumber;
                }
            }
            return maxPage;
        }

        static ScrapingBrowser browser = new ScrapingBrowser();
        static HtmlNode GetHtml(string url)
        {
            browser.Encoding = Encoding.UTF8;

            WebPage webPage = browser.NavigateToPage(new Uri(url));
            return webPage.Html;
        }
    }
    public class Event
    {
        public int Id { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }
        public DateTime DateStart { get; set; }
        public DateTime DateEnd { get; set; }
        public string ImageLink { get; set; }
        public string Price { get; set; }
        public string TicketLink { get; set; }
        public bool Updated { get; set; }
        public string Location { get; set; }
        public string Category { get; set; }
    }
    public class Website
    {
        public int Id { get; set; }
        public string Url { get; set; }
        public string TitlePath { get; set; }
        public string LocationPath { get; set; }
        public string PricePath { get; set; }
        public string DatePath { get; set; }
        public string ImagePath { get; set; }
        public string TicketPath { get; set; }
        public string UrlExtensionForEvent { get; set; }
        public string EventLink { get; set; }
        public string CategoryLink { get; set; }
        public string PagerLink { get; set; }
    }
    public class BlacklistedPage
    {
        public int Id { get; set; }
        public string Url { get; set; }
        public string Comment { get; set; }
    }
    public class BlacklistedCategoryName
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
    public class Category
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string SourceUrl { get; set; }
    }
    public class City
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }
    public class WebDbContext : DbContext
    {
        public DbSet<Event> Events { get; set; }
        public DbSet<Website> Websites { get; set; }
        public DbSet<BlacklistedPage> BlacklistedPages { get; set; }
        public DbSet<BlacklistedCategoryName> BlacklistedCategoryNames { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<City> Cities { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer(@"Server=tcp:event-radar-server.database.windows.net,1433;Initial Catalog=eventRadarDB;Persist Security Info=False;User ID=sadmin;Password=Epiktetas1;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;");
        }
    }
    public interface IEventRepository
    {
        Task CreateAsync(Event eventObject);
        Task DeleteAsync(Event eventObject);
        Task<Event?> GetAsync(int eventId);
        Task<IReadOnlyList<Event>> GetManyAsync();
        Task UpdateAsync(Event eventObject);
    }

    public class EventRepository : IEventRepository
    {
        private readonly WebDbContext _webDbContext;
        public EventRepository(WebDbContext webDbContext)
        {
            _webDbContext = webDbContext;
        }
        public async Task<Event?> GetAsync(int eventId)
        {
            return await _webDbContext.Events.FirstOrDefaultAsync(o => o.Id == eventId);
        }
        public async Task<IReadOnlyList<Event>> GetManyAsync()
        {
            return await _webDbContext.Events.ToListAsync();
        }
        public async Task CreateAsync(Event eventObject)
        {
            _webDbContext.Events.Add(eventObject);
            await _webDbContext.SaveChangesAsync();
        }
        public async Task UpdateAsync(Event eventObject)
        {
            _webDbContext.Events.Update(eventObject);
            await _webDbContext.SaveChangesAsync();
        }
        public async Task DeleteAsync(Event eventObject)
        {
            _webDbContext.Events.Remove(eventObject);
            await _webDbContext.SaveChangesAsync();
        }
    }
    public interface IBlacklistedPageRepository
    {
        Task CreateAsync(BlacklistedPage blacklistedPage);
        Task DeleteAsync(BlacklistedPage blacklistedPage);
        Task<BlacklistedPage?> GetAsync(int blacklistePageID);
        Task<IReadOnlyList<BlacklistedPage>> GetManyAsync();
        Task UpdateAsync(BlacklistedPage blacklistedPage);
    }
    public class BlacklistedPageRepository : IBlacklistedPageRepository
    {
        private readonly WebDbContext _webDbContext;
        public BlacklistedPageRepository(WebDbContext webDbContext)
        {
            _webDbContext = webDbContext;
        }
        public async Task<BlacklistedPage?> GetAsync(int blacklistedPageId)
        {
            return await _webDbContext.BlacklistedPages.FirstOrDefaultAsync(o => o.Id == blacklistedPageId);
        }
        public async Task<IReadOnlyList<BlacklistedPage>> GetManyAsync()
        {
            return await _webDbContext.BlacklistedPages.ToListAsync();
        }
        public async Task CreateAsync(BlacklistedPage blacklistedPage)
        {
            _webDbContext.BlacklistedPages.Add(blacklistedPage);
            await _webDbContext.SaveChangesAsync();
        }
        public async Task UpdateAsync(BlacklistedPage blacklistedPage)
        {
            _webDbContext.BlacklistedPages.Update(blacklistedPage);
            await _webDbContext.SaveChangesAsync();
        }
        public async Task DeleteAsync(BlacklistedPage blacklistedPage)
        {
            _webDbContext.BlacklistedPages.Remove(blacklistedPage);
            await _webDbContext.SaveChangesAsync();
        }
    }
    public interface IWebsiteRepository
    {
        Task CreateAsync(Website website);
        Task UpdateAsync(Website website);
        Task DeleteAsync(Website website);
        Task<Website?> GetAsync(int websiteId);
        Task<IReadOnlyList<Website>> GetManyAsync();
    }
    public class WebsiteRepository : IWebsiteRepository
    {
        private readonly WebDbContext _webDbContext;
        public WebsiteRepository(WebDbContext webDbContext)
        {
            _webDbContext = webDbContext;
        }
        public async Task<Website?> GetAsync(int websiteId)
        {
            return await _webDbContext.Websites.FirstOrDefaultAsync(o => o.Id == websiteId);
        }
        public async Task<IReadOnlyList<Website>> GetManyAsync()
        {
            return await _webDbContext.Websites.ToListAsync();
        }
        public async Task CreateAsync(Website website)
        {
            _webDbContext.Websites.Add(website);
            await _webDbContext.SaveChangesAsync();
        }
        public async Task UpdateAsync(Website website)
        {
            _webDbContext.Websites.Update(website);
            await _webDbContext.SaveChangesAsync();
        }
        public async Task DeleteAsync(Website website)
        {
            _webDbContext.Websites.Remove(website);
            await _webDbContext.SaveChangesAsync();
        }
    }
    public interface IBlacklistedCategoryNameRepository
    {
        Task CreateAsync(BlacklistedCategoryName categoryName);
        Task DeleteAsync(BlacklistedCategoryName categoryName);
        Task UpdateAsync(BlacklistedCategoryName categoryName);
        Task<BlacklistedCategoryName?> GetAsync(int blacklistedCategoryId);
        Task<IReadOnlyList<BlacklistedCategoryName>> GetManyAsync();
    }
    public class BlacklistedCategoryNameRepository : IBlacklistedCategoryNameRepository
    {
        private readonly WebDbContext _webDbContext;
        public BlacklistedCategoryNameRepository(WebDbContext webDbContext)
        {
            _webDbContext = webDbContext;
        }
        public async Task<IReadOnlyList<BlacklistedCategoryName>> GetManyAsync()
        {
            return await _webDbContext.BlacklistedCategoryNames.ToListAsync();
        }
        public async Task<BlacklistedCategoryName?> GetAsync(int blacklistedCategoryId)
        {
            return await _webDbContext.BlacklistedCategoryNames.FirstOrDefaultAsync(o => o.Id == blacklistedCategoryId);
        }
        public async Task CreateAsync(BlacklistedCategoryName blacklistedCategoryName)
        {
            _webDbContext.BlacklistedCategoryNames.Add(blacklistedCategoryName);
            await _webDbContext.SaveChangesAsync();
        }
        public async Task UpdateAsync(BlacklistedCategoryName blacklistedCategoryName)
        {
            _webDbContext.BlacklistedCategoryNames.Add(blacklistedCategoryName);
            await _webDbContext.SaveChangesAsync();
        }
        public async Task DeleteAsync(BlacklistedCategoryName blacklistedCategoryName)
        {
            _webDbContext.BlacklistedCategoryNames.Remove(blacklistedCategoryName);
            await _webDbContext.SaveChangesAsync();
        }
    }
    public interface ICategoryRepository
    {
        Task CreateAsync(Category category);
        Task DeleteAsync(Category category);
        Task UpdateAsync(Category category);
        Task<Category?> GetAsync(int categoryId);
        Task<IReadOnlyList<Category>> GetManyAsync();
    }
    public class CategoryRepository : ICategoryRepository
    {
        private readonly WebDbContext _webDbContext;
        public CategoryRepository(WebDbContext webDbContext)
        {
            _webDbContext = webDbContext;
        }
        public async Task<Category?> GetAsync(int categoryId)
        {
            return await _webDbContext.Categories.FirstOrDefaultAsync(o => o.Id == categoryId);
        }
        public async Task<IReadOnlyList<Category>> GetManyAsync()
        {
            return await _webDbContext.Categories.ToListAsync();
        }
        public async Task CreateAsync(Category category)
        {
            _webDbContext.Categories.Add(category);
            await _webDbContext.SaveChangesAsync();
        }
        public async Task UpdateAsync(Category category)
        {
            _webDbContext.Categories.Update(category);
            await _webDbContext.SaveChangesAsync();
        }
        public async Task DeleteAsync(Category category)
        {
            _webDbContext.Categories.Remove(category);
            await _webDbContext.SaveChangesAsync();
        }
    }
    public interface ICityRepository
    {
        Task CreateAsync(City city);
        Task DeleteAsync(City city);
        Task<City?> GetAsync(int cityId);
        Task<IReadOnlyList<City>> GetManyAsync();
    }
    public class CityRepository : ICityRepository
    {
        private readonly WebDbContext _webDbContext;
        public CityRepository(WebDbContext webDbContext)
        {
            _webDbContext = webDbContext;
        }
        public async Task<City?> GetAsync(int cityId)
        {
            return await _webDbContext.Cities.FirstOrDefaultAsync(o => o.Id == cityId);
        }
        public async Task<IReadOnlyList<City>> GetManyAsync()
        {
            return await _webDbContext.Cities.ToListAsync();
        }
        public async Task CreateAsync(City city)
        {
            _webDbContext.Cities.Add(city);
            await _webDbContext.SaveChangesAsync();
        }
        public async Task DeleteAsync(City city)
        {
            _webDbContext.Cities.Remove(city);
            await _webDbContext.SaveChangesAsync();
        }
    }
}