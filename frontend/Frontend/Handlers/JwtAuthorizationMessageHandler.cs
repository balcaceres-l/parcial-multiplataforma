using System.Net.Http.Headers;

namespace Frontend.Handlers;

public class JwtAuthorizationMessageHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public JwtAuthorizationMessageHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context != null)
        {
            var token = context.Session.GetString("JWToken")
                        ?? context.User.FindFirst("JWToken")?.Value;

            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
