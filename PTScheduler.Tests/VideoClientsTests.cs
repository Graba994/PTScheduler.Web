using System.Net;
using System.Text;
using FluentAssertions;
using PTScheduler.Portal.Services;
using PTScheduler.Portal.Services.Social;
using Xunit;

namespace PTScheduler.Tests;

public class VideoClientsTests
{
    private sealed class Fake(Func<HttpRequestMessage, (HttpStatusCode, string, string?)> respond) : HttpMessageHandler
    {
        public List<(string Method, string Url, string? Range, long? Length, string Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is ByteArrayContent && request.Content.Headers.ContentType?.MediaType == "video/mp4" ? "<video>" : request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Calls.Add((request.Method.Method, request.RequestUri!.ToString(), request.Content?.Headers.ContentRange?.ToString(), request.Content?.Headers.ContentLength, body));
            var (code, json, location) = respond(request);
            var resp = new HttpResponseMessage(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            if (location is not null) resp.Headers.Location = new Uri(location);
            return resp;
        }
    }

    private sealed class Factory(HttpMessageHandler h) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(h, disposeHandler: false);
    }

    [Fact]
    public async Task TikTok_file_upload_then_status()
    {
        var fake = new Fake(req => req.RequestUri!.AbsolutePath switch
        {
            "/v2/post/publish/video/init/" => (HttpStatusCode.OK, "{\"data\":{\"publish_id\":\"pub1\",\"upload_url\":\"https://upload.tiktok/x\"},\"error\":{\"code\":\"ok\"}}", null),
            "/x" => (HttpStatusCode.Created, "{}", null),
            "/v2/post/publish/status/fetch/" => (HttpStatusCode.OK, "{\"data\":{\"status\":\"PUBLISH_COMPLETE\",\"publicaly_available_post_id\":[7311]},\"error\":{\"code\":\"ok\"}}", null),
            _ => (HttpStatusCode.OK, "{}", null)
        });
        var tt = new TikTokClient(new Factory(fake));
        (await tt.UploadAsync("T", new byte[1000], "Opis", "SELF_ONLY")).Should().Be("pub1");
        fake.Calls[0].Body.Should().Contain("\"source\":\"FILE_UPLOAD\"").And.Contain("\"video_size\":1000").And.Contain("\"privacy_level\":\"SELF_ONLY\"");
        fake.Calls[1].Method.Should().Be("PUT");
        fake.Calls[1].Range.Should().Be("bytes 0-999/1000");
        var (status, postId, _) = await tt.StatusAsync("T", "pub1");
        status.Should().Be("PUBLISH_COMPLETE");
        postId.Should().Be("7311");
        tt.AuthorizeUrl("ck", "https://hantly.pl/cb", "s1").Should().Contain("scope=user.info.basic%2Cvideo.publish%2Cvideo.upload").And.Contain("state=s1");
    }

    [Fact]
    public async Task TikTok_error_and_expired_token()
    {
        var fake = new Fake(_ => (HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"access_token_invalid\",\"message\":\"The access token is invalid\"}}", null));
        var act = () => new TikTokClient(new Factory(fake)).StatusAsync("T", "p");
        (await act.Should().ThrowAsync<InstagramApiException>()).Which.TokenInvalid.Should().BeTrue();
    }

    [Fact]
    public async Task YouTube_resumable_upload_as_short()
    {
        var fake = new Fake(req => req.Method == HttpMethod.Post
            ? (HttpStatusCode.OK, "{}", "https://www.googleapis.com/upload/youtube/v3/videos?upload_id=abc")
            : (HttpStatusCode.OK, "{\"id\":\"vid42\"}", null));
        var yt = new YouTubeClient(new Factory(fake));
        (await yt.UploadAsync("T", new byte[500], "Plan w telefonie #Shorts", "Opis", ["trener"], "private")).Should().Be("vid42");
        fake.Calls[0].Url.Should().Contain("uploadType=resumable&part=snippet,status");
        fake.Calls[0].Body.Should().Contain("\"privacyStatus\":\"private\"").And.Contain("\"selfDeclaredMadeForKids\":false");
        fake.Calls[1].Url.Should().Contain("upload_id=abc");
        YouTubeClient.AuthorizeUrl("cid", "https://hantly.pl/cb", "s").Should().Contain("access_type=offline").And.Contain("youtube.upload");
    }

    [Fact]
    public async Task Google_invalid_grant_means_reconnect()
    {
        var fake = new Fake(_ => (HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\",\"error_description\":\"Token has been expired or revoked.\"}", null));
        var act = () => new YouTubeClient(new Factory(fake)).RefreshAsync("id", "secret", "r");
        var ex = (await act.Should().ThrowAsync<InstagramApiException>()).Which;
        ex.TokenInvalid.Should().BeTrue();
        ex.Message.Should().Contain("expired or revoked");
    }
}
