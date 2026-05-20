using DnsClient.Protocol;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PKISharp.WACS.Plugins.ValidationPlugins.Simply
{
    public class SimplyDnsClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl = "https://api.simply.com/2";

        public SimplyDnsClient(string account, string apiKey, HttpClient httpClient)
        {
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", EncodeBasicAuth(account, apiKey));
        }

        public async Task<List<Product>> GetAllProducts()
        {
            return await GetProductsAsync();
        }

        public async Task CreateRecordAsync(string objectId, string domain, string value)
        {
            await CreateRecordAsync(objectId, new DnsRecord
            {
                Type = "TXT",
                Name = domain,
                Data = value,
                Ttl = 3600,
            });
        }

        public async Task DeleteRecordAsync(Product product, string domain, string value)
        {
            if (string.IsNullOrEmpty(product.Object))
            {
                throw new InvalidOperationException("Product has no object id");
            }
            var zoneName = product.Domain?.NameIdn ?? product.Domain?.Name;
            if (string.IsNullOrEmpty(zoneName))
            {
                throw new InvalidOperationException($"Product {product.Object} has no domain name");
            }

            var records = await GetRecordsAsync(product.Object);
            var matching = records
                .Where(x => x.Type == "TXT" && $"{x.Name}.{zoneName}" == domain && x.Data == value)
                .ToList();

            // ACME cleanup must be idempotent. If a previous run already
            // removed the record (or the create step failed before adding
            // it) there is nothing to delete and that is a success.
            foreach (var record in matching)
            {
                await DeleteRecordAsync(product.Object, record.RecordId);
            }
        }

        private async Task<List<Product>> GetProductsAsync()
        {
            using var response = await _httpClient.GetAsync(_baseUrl + "/my/products/");
            await EnsureSuccessOrThrowAsync(response, "GET /my/products/");
            await using var stream = await response.Content.ReadAsStreamAsync();
            var products = await JsonSerializer.DeserializeAsync<ProductList>(stream);
            if (products == null || products.Products == null)
            {
                throw new Exception("Unable to retrieve products");
            }
            return products.Products;
        }

        private async Task CreateRecordAsync(string objectId, DnsRecord record)
        {
            using var content = new StringContent(JsonSerializer.Serialize(record), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(_baseUrl + $"/my/products/{WebUtility.UrlEncode(objectId)}/dns/records/", content);
            await EnsureSuccessOrThrowAsync(response, "POST /dns/records/");
        }

        private async Task DeleteRecordAsync(string objectId, int recordId)
        {
            using var response = await _httpClient.DeleteAsync(_baseUrl + $"/my/products/{WebUtility.UrlEncode(objectId)}/dns/records/{recordId}/");
            await EnsureSuccessOrThrowAsync(response, $"DELETE /dns/records/{recordId}/");
        }

        private async Task<List<DnsRecord>> GetRecordsAsync(string objectId)
        {
            using var response = await _httpClient.GetAsync(_baseUrl + $"/my/products/{WebUtility.UrlEncode(objectId)}/dns/records/");
            await EnsureSuccessOrThrowAsync(response, "GET /dns/records/");
            await using var stream = await response.Content.ReadAsStreamAsync();
            var records = await JsonSerializer.DeserializeAsync<DnsRecordList>(stream);
            if (records == null || records.Records == null)
            {
                throw new InvalidOperationException();
            }
            return records.Records;
        }

        private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response, string operation)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }
            var detail = await TryReadMessageAsync(response);
            var prefix = $"Simply.com API {operation} failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            throw new HttpRequestException(
                string.IsNullOrEmpty(detail) ? prefix : $"{prefix}: {detail}");
        }

        private static async Task<string?> TryReadMessageAsync(HttpResponseMessage response)
        {
            var body = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    // Simply error responses are {"error": "..."}.
                    // The "status" field is hardcoded 200 even on errors,
                    // so trust the HTTP status code, not the body.
                    if (doc.RootElement.TryGetProperty("error", out var err) &&
                        err.ValueKind == JsonValueKind.String)
                    {
                        return err.GetString();
                    }
                    if (doc.RootElement.TryGetProperty("message", out var msg) &&
                        msg.ValueKind == JsonValueKind.String)
                    {
                        return msg.GetString();
                    }
                }
            }
            catch (JsonException)
            {
            }
            return null;
        }

        private static string EncodeBasicAuth(string account, string apiKey)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(WebUtility.UrlEncode(account) + ":" + WebUtility.UrlEncode(apiKey)));
        }
    }
}