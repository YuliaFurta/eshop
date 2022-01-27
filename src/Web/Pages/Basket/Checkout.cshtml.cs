using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Web.Interfaces;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using Newtonsoft.Json;
using System.Text;
using Azure.Storage.Blobs;

namespace Microsoft.eShopWeb.Web.Pages.Basket;

[Authorize]
public class CheckoutModel : PageModel
{
    private readonly IBasketService _basketService;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IOrderService _orderService;
    private string _username = null;
    private readonly IBasketViewModelService _basketViewModelService;
    private readonly IAppLogger<CheckoutModel> _logger;
    private AppSettings _appSettings { get; set; }
    private AzureStorageConfig _storageConfig { get; set; }


    public CheckoutModel(IBasketService basketService,
        IBasketViewModelService basketViewModelService,
        SignInManager<ApplicationUser> signInManager,
        IOrderService orderService,
        IAppLogger<CheckoutModel> logger,
        IOptions<AppSettings> appSettings,
        IOptions<AzureStorageConfig> storageConfig
     )
    {
        _basketService = basketService;
        _signInManager = signInManager;
        _orderService = orderService;
        _basketViewModelService = basketViewModelService;
        _logger = logger;
        _appSettings = appSettings.Value;
        _storageConfig = storageConfig.Value;
    }

    public BasketViewModel BasketModel { get; set; } = new BasketViewModel();

    public async Task OnGet()
    {
        await SetBasketModelAsync();

        var Url = _appSettings.AzureFunctionURL;

        dynamic content = BasketModel.Items.Select(x => new { x.Id, x.ProductName, x.Quantity });

        _logger.LogWarning("_storageConfig.ConnectionString");
        _logger.LogWarning(_storageConfig.ConnectionString);
        _logger.LogWarning(_storageConfig.FileContainerName);

        //BlobServiceClient blobServiceClient = new BlobServiceClient(_storageConfig.ConnectionString);
        //BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(_storageConfig.FileContainerName);
        //var container = containerClient.CreateIfNotExistsAsync();

        //var rand = new Random();
        //int a = rand.Next();
        //BlobClient blobClient = containerClient.GetBlobClient(a + ".json");

        //_logger.LogWarning("fileName");
        //_logger.LogWarning(a + ".json");

        //string json = JsonConvert.SerializeObject(BasketModel);
        //await blobClient.UploadAsync(GenerateStreamFromString(json));

        using (var client = new HttpClient())
        using (var request = new HttpRequestMessage(HttpMethod.Post, Url))
        using (var httpContent = CreateHttpContent(content))
        {
            request.Content = httpContent;

            using (var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false))
            {

                var result = response.Content.ReadAsStringAsync().Result;
                _logger.LogWarning("result");
                _logger.LogWarning(result);

            }
        }
    }

    public static Stream GenerateStreamFromString(string s)
    {
        var stream = new MemoryStream();
        var writer = new StreamWriter(stream);
        writer.Write(s);
        writer.Flush();
        stream.Position = 0;
        return stream;
    }

    public async Task<IActionResult> OnPost(IEnumerable<BasketItemViewModel> items)
    {
        try
        {
            await SetBasketModelAsync();

            if (!ModelState.IsValid)
            {
                return BadRequest();
            }

            var updateModel = items.ToDictionary(b => b.Id.ToString(), b => b.Quantity);
            await _basketService.SetQuantities(BasketModel.Id, updateModel);
            await _orderService.CreateOrderAsync(BasketModel.Id, new Address("123 Main St.", "Kent", "OH", "United States", "44240"));
            await _basketService.DeleteBasketAsync(BasketModel.Id);
        }
        catch (EmptyBasketOnCheckoutException emptyBasketOnCheckoutException)
        {
            //Redirect to Empty Basket page
            _logger.LogWarning(emptyBasketOnCheckoutException.Message);
            return RedirectToPage("/Basket/Index");
        }

        return RedirectToPage("Success");
    }

    private static HttpContent CreateHttpContent(object content)
    {
        HttpContent httpContent = null;

        if (content != null)
        {
            var ms = new MemoryStream();
            SerializeJsonIntoStream(content, ms);
            ms.Seek(0, SeekOrigin.Begin);
            httpContent = new StreamContent(ms);
            httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        return httpContent;
    }

    public static void SerializeJsonIntoStream(object value, Stream stream)
    {
        using (var sw = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
        using (var jtw = new JsonTextWriter(sw) { Formatting = Formatting.None })
        {
            var js = new JsonSerializer();
            js.Serialize(jtw, value);
            jtw.Flush();
        }
    }

    private async Task SetBasketModelAsync()
    {
        if (_signInManager.IsSignedIn(HttpContext.User))
        {
            BasketModel = await _basketViewModelService.GetOrCreateBasketForUser(User.Identity.Name);
        }
        else
        {
            GetOrSetBasketCookieAndUserName();
            BasketModel = await _basketViewModelService.GetOrCreateBasketForUser(_username);
        }
    }

    private void GetOrSetBasketCookieAndUserName()
    {
        if (Request.Cookies.ContainsKey(Constants.BASKET_COOKIENAME))
        {
            _username = Request.Cookies[Constants.BASKET_COOKIENAME];
        }
        if (_username != null) return;

        _username = Guid.NewGuid().ToString();
        var cookieOptions = new CookieOptions();
        cookieOptions.Expires = DateTime.Today.AddYears(10);
        Response.Cookies.Append(Constants.BASKET_COOKIENAME, _username, cookieOptions);
    }
}
