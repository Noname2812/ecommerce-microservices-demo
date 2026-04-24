using Microsoft.AspNetCore.Http;
using UrbanX.Gateway.Application.Configuration;

namespace UrbanX.Gateway.Application.Abstractions;

public interface IEndpointAccessRegistry
{
    EndpointAccessResult GetAccessFor(string method, PathString path);
}
