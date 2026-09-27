using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AgriGuard.Domain.Identity;
using AgriGuard.IntegrationTests.Infrastructure;

namespace AgriGuard.IntegrationTests;

/// <summary>Leaf photos on a case: what is accepted, who may see them, and that nothing is stored twice.</summary>
[Collection(ApiCollection.Name)]
public sealed class CasePhotoTests(AgriGuardApiFactory factory)
{
    private static byte[] Jpeg(int size = 2048, byte seed = 1)
    {
        var bytes = new byte[size];
        new Random(seed).NextBytes(bytes);
        // Only the signature matters to the check; the rest stands in for the image data.
        bytes[0] = 0xFF; bytes[1] = 0xD8; bytes[2] = 0xFF; bytes[3] = 0xE0;
        return bytes;
    }

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8];

    private static Task<HttpResponseMessage> Upload(HttpClient client, Guid caseId, byte[] bytes, string fileName = "leaf.jpg", string contentType = "image/jpeg")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent { { file, "file", fileName } };
        return client.PostAsync($"/api/cases/{caseId}/photos", form);
    }

    private async Task<(CaseFixtures.FarmSetup Setup, HttpClient Farmer, Guid CaseId)> CaseAsync()
    {
        var setup = await factory.SeedTomatoPlotAsync();
        var farmer = await factory.SignedInAsAsync(setup.Farmer);
        var caseId = (await farmer.ReportCaseAsync(setup)).GetProperty("id").GetGuid();
        return (setup, farmer, caseId);
    }

    [Fact]
    public async Task A_photo_is_stored_listed_on_the_case_and_served_back_privately()
    {
        var (_, farmer, caseId) = await CaseAsync();
        var bytes = Jpeg();

        var response = await Upload(farmer, caseId, bytes);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var photo = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("image/jpeg", photo.GetProperty("contentType").GetString());
        Assert.Equal(bytes.Length, photo.GetProperty("sizeBytes").GetInt64());

        var detail = await farmer.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}");
        var listed = Assert.Single(detail.GetProperty("photos").EnumerateArray());
        Assert.Equal(photo.GetProperty("id").GetGuid(), listed.GetProperty("id").GetGuid());

        var image = await farmer.GetAsync($"/api/cases/{caseId}/photos/{photo.GetProperty("id").GetGuid()}");
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType!.MediaType);
        Assert.Equal(bytes, await image.Content.ReadAsByteArrayAsync());
        Assert.True(image.Headers.CacheControl!.Private);
        Assert.Equal("nosniff", image.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task Uploading_the_same_photo_again_returns_the_stored_one()
    {
        var (_, farmer, caseId) = await CaseAsync();
        var bytes = Jpeg();

        var first = await (await Upload(farmer, caseId, bytes)).Content.ReadFromJsonAsync<JsonElement>();
        var retry = await Upload(farmer, caseId, bytes);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(first.GetProperty("id").GetGuid(), (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        var detail = await farmer.GetFromJsonAsync<JsonElement>($"/api/cases/{caseId}");
        Assert.Single(detail.GetProperty("photos").EnumerateArray());
    }

    [Fact]
    public async Task A_file_that_only_claims_to_be_an_image_is_refused()
    {
        var (_, farmer, caseId) = await CaseAsync();

        var response = await Upload(farmer, caseId, Encoding.UTF8.GetBytes("<script>alert(1)</script>"), "leaf.jpg", "image/jpeg");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("not a JPEG, PNG or WebP", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_photo_over_two_megabytes_is_refused()
    {
        var (_, farmer, caseId) = await CaseAsync();

        var response = await Upload(farmer, caseId, Jpeg(2 * 1024 * 1024 + 1));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_case_holds_at_most_three_photos()
    {
        var (_, farmer, caseId) = await CaseAsync();
        for (byte i = 1; i <= 3; i++)
            Assert.Equal(HttpStatusCode.Created, (await Upload(farmer, caseId, Jpeg(seed: i))).StatusCode);

        var fourth = await Upload(farmer, caseId, Jpeg(seed: 4));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, fourth.StatusCode);
        Assert.Equal("PHOTO_LIMIT_REACHED", (await fourth.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task The_type_and_name_come_from_the_file_not_from_what_the_client_says()
    {
        var (_, farmer, caseId) = await CaseAsync();

        // A PNG labelled as JPEG, with a path and markup in its name.
        var response = await Upload(farmer, caseId, Png, @"..\..\etc/<evil>.jpg", "image/jpeg");

        var photo = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("image/png", photo.GetProperty("contentType").GetString());
        Assert.Equal("evil.png", photo.GetProperty("fileName").GetString());
    }

    [Fact]
    public async Task The_district_agronomist_sees_the_photos_and_another_farmer_does_not()
    {
        var (setup, farmer, caseId) = await CaseAsync();
        var photoId = (await (await Upload(farmer, caseId, Jpeg())).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var agronomist = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.FieldAgronomist, districtId: setup.DistrictId));
        var stranger = await factory.SignedInAsAsync(await factory.CreateUserAsync(UserRole.Farmer));

        Assert.Equal(HttpStatusCode.OK, (await agronomist.GetAsync($"/api/cases/{caseId}/photos/{photoId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await stranger.GetAsync($"/api/cases/{caseId}/photos/{photoId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Upload(stranger, caseId, Jpeg(seed: 9))).StatusCode);
    }

    [Fact]
    public async Task No_photos_are_added_to_a_closed_case()
    {
        var (_, farmer, caseId) = await CaseAsync();
        await farmer.PatchAsJsonAsync($"/api/cases/{caseId}/status", new { status = "Closed" });

        var response = await Upload(farmer, caseId, Jpeg());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }
}
