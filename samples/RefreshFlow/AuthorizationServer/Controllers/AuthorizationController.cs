﻿/*
 * Licensed under the Apache License, Version 2.0 (http://www.apache.org/licenses/LICENSE-2.0)
 * See https://github.com/openiddict/openiddict-core for more information concerning
 * the license and the contributors participating to this project.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AspNet.Security.OpenIdConnect.Extensions;
using AspNet.Security.OpenIdConnect.Primitives;
using AuthorizationServer.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace AuthorizationServer.Controllers
{
    public class AuthorizationController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public AuthorizationController(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [HttpPost("~/connect/token"), Produces("application/json")]
        public async Task<IActionResult> Exchange()
        {
            var request = HttpContext.GetOpenIdConnectRequest();
            if (request.IsPasswordGrantType())
            {
                var user = await _userManager.FindByNameAsync(request.Username);
                if (user == null)
                {
                    var properties = new AuthenticationProperties(new Dictionary<string, string>
                    {
                        [OpenIdConnectConstants.Properties.Error] = OpenIdConnectConstants.Errors.InvalidGrant,
                        [OpenIdConnectConstants.Properties.ErrorDescription] = "The username/password couple is invalid."
                    });

                    return Forbid(properties, OpenIddictServerDefaults.AuthenticationScheme);
                }

                // Validate the username/password parameters and ensure the account is not locked out.
                var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
                if (!result.Succeeded)
                {
                    var properties = new AuthenticationProperties(new Dictionary<string, string>
                    {
                        [OpenIdConnectConstants.Properties.Error] = OpenIdConnectConstants.Errors.InvalidGrant,
                        [OpenIdConnectConstants.Properties.ErrorDescription] = "The username/password couple is invalid."
                    });

                    return Forbid(properties, OpenIddictServerDefaults.AuthenticationScheme);
                }

                // Create a new ClaimsPrincipal containing the claims that
                // will be used to create an id_token, a token or a code.
                var principal = await _signInManager.CreateUserPrincipalAsync(user);

                // Create a new authentication ticket holding the user identity.
                var ticket = new AuthenticationTicket(principal,
                    new AuthenticationProperties(),
                    OpenIddictServerDefaults.AuthenticationScheme);

                // Set the list of scopes granted to the client application.
                // Note: the offline_access scope must be granted
                // to allow OpenIddict to return a refresh token.
                ticket.SetScopes(new[]
                {
                    OpenIdConnectConstants.Scopes.OpenId,
                    OpenIdConnectConstants.Scopes.Email,
                    OpenIdConnectConstants.Scopes.Profile,
                    OpenIdConnectConstants.Scopes.OfflineAccess,
                    OpenIddictConstants.Scopes.Roles
                }.Intersect(request.GetScopes()));

                ticket.SetResources("resource_server");

                foreach (var claim in ticket.Principal.Claims)
                {
                    claim.SetDestinations(GetDestinations(claim, ticket));
                }

                return SignIn(ticket.Principal, ticket.Properties, ticket.AuthenticationScheme);
            }

            else if (request.IsRefreshTokenGrantType())
            {
                // Retrieve the claims principal stored in the refresh token.
                var info = await HttpContext.AuthenticateAsync(OpenIddictServerDefaults.AuthenticationScheme);

                // Retrieve the user profile corresponding to the refresh token.
                // Note: if you want to automatically invalidate the refresh token
                // when the user password/roles change, use the following line instead:
                // var user = _signInManager.ValidateSecurityStampAsync(info.Principal);
                var user = await _userManager.GetUserAsync(info.Principal);
                if (user == null)
                {
                    var properties = new AuthenticationProperties(new Dictionary<string, string>
                    {
                        [OpenIdConnectConstants.Properties.Error] = OpenIdConnectConstants.Errors.InvalidGrant,
                        [OpenIdConnectConstants.Properties.ErrorDescription] = "The refresh token is no longer valid."
                    });

                    return Forbid(properties, OpenIddictServerDefaults.AuthenticationScheme);
                }

                // Ensure the user is still allowed to sign in.
                if (!await _signInManager.CanSignInAsync(user))
                {
                    var properties = new AuthenticationProperties(new Dictionary<string, string>
                    {
                        [OpenIdConnectConstants.Properties.Error] = OpenIdConnectConstants.Errors.InvalidGrant,
                        [OpenIdConnectConstants.Properties.ErrorDescription] = "The user is no longer allowed to sign in."
                    });

                    return Forbid(properties, OpenIddictServerDefaults.AuthenticationScheme);
                }

                // Create a new ClaimsPrincipal containing the claims that
                // will be used to create an id_token, a token or a code.
                var principal = await _signInManager.CreateUserPrincipalAsync(user);

                // Create a new authentication ticket, but reuse the properties stored
                // in the refresh token, including the scopes originally granted.
                var ticket = new AuthenticationTicket(principal, info.Properties,
                    OpenIddictServerDefaults.AuthenticationScheme);

                foreach (var claim in ticket.Principal.Claims)
                {
                    claim.SetDestinations(GetDestinations(claim, ticket));
                }

                return SignIn(ticket.Principal, ticket.Properties, ticket.AuthenticationScheme);
            }

            throw new NotImplementedException("The specified grant type is not implemented.");
        }

        private IEnumerable<string> GetDestinations(Claim claim, AuthenticationTicket ticket)
        {
            // Note: by default, claims are NOT automatically included in the access and identity tokens.
            // To allow OpenIddict to serialize them, you must attach them a destination, that specifies
            // whether they should be included in access tokens, in identity tokens or in both.

            switch (claim.Type)
            {
                case OpenIdConnectConstants.Claims.Name:
                    yield return OpenIdConnectConstants.Destinations.AccessToken;

                    if (ticket.HasScope(OpenIdConnectConstants.Scopes.Profile))
                        yield return OpenIdConnectConstants.Destinations.IdentityToken;

                    yield break;

                case OpenIdConnectConstants.Claims.Email:
                    yield return OpenIdConnectConstants.Destinations.AccessToken;

                    if (ticket.HasScope(OpenIdConnectConstants.Scopes.Email))
                        yield return OpenIdConnectConstants.Destinations.IdentityToken;

                    yield break;

                case OpenIdConnectConstants.Claims.Role:
                    yield return OpenIdConnectConstants.Destinations.AccessToken;

                    if (ticket.HasScope(OpenIddictConstants.Scopes.Roles))
                        yield return OpenIdConnectConstants.Destinations.IdentityToken;

                    yield break;

                // Never include the security stamp in the access and identity tokens, as it's a secret value.
                case "AspNet.Identity.SecurityStamp": yield break;

                default:
                    yield return OpenIdConnectConstants.Destinations.AccessToken;
                    yield break;
            }
        }
    }
}
