﻿using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ombi.Api.Plex;
using Ombi.Core.Settings;
using Ombi.Core.Settings.Models.External;
using Ombi.Helpers;
using Ombi.Settings.Settings.Models;
using Ombi.Store.Entities;

namespace Ombi.Schedule.Jobs.Plex
{
    public class PlexUserImporter : IPlexUserImporter
    {
        public PlexUserImporter(IPlexApi api, UserManager<OmbiUser> um, ILogger<PlexUserImporter> log,
            ISettingsService<PlexSettings> plexSettings, ISettingsService<UserManagementSettings> ums)
        {
            _api = api;
            _userManager = um;
            _log = log;
            _plexSettings = plexSettings;
            _userManagementSettings = ums;
        }

        private readonly IPlexApi _api;
        private readonly UserManager<OmbiUser> _userManager;
        private readonly ILogger<PlexUserImporter> _log;
        private readonly ISettingsService<PlexSettings> _plexSettings;
        private readonly ISettingsService<UserManagementSettings> _userManagementSettings;


        public async Task Start()
        {
            var userManagementSettings = await _userManagementSettings.GetSettingsAsync();
            if (!userManagementSettings.ImportMediaServerUsers)
            {
                return;
            }
            var settings = await _plexSettings.GetSettingsAsync();
            if (!settings.Enable)
            {
                return;
            }
            var allUsers = await _userManager.Users.Where(x => x.UserType == UserType.PlexUser).ToListAsync();
            foreach (var server in settings.Servers)
            {
                if (string.IsNullOrEmpty(server.PlexAuthToken))
                {
                    continue;
                }

                var users = await _api.GetUsers(server.PlexAuthToken);

                foreach (var plexUsers in users.User)
                {
                    // Check if this Plex User already exists
                    // We are using the Plex USERNAME and Not the TITLE, the Title is for HOME USERS
                    var existingPlexUser = allUsers.FirstOrDefault(x => x.ProviderUserId == plexUsers.Id);
                    if (existingPlexUser == null)
                    {
                        // Create this users
                        // We do not store a password against the user since they will authenticate via Plex
                        var newUser = new OmbiUser
                        {
                            UserType = UserType.PlexUser,
                            UserName = plexUsers.Username,
                            ProviderUserId = plexUsers.Id,
                            Email = plexUsers.Email,
                            Alias = string.Empty
                        };
                        var result = await _userManager.CreateAsync(newUser);
                        if (!result.Succeeded)
                        {
                            foreach (var identityError in result.Errors)
                            {
                                _log.LogError(LoggingEvents.PlexUserImporter, identityError.Description);
                            }
                            continue;
                        }
                        // TODO Set default permissions/roles
                        if (userManagementSettings.DefaultRoles.Any())
                        {
                            foreach (var defaultRole in userManagementSettings.DefaultRoles)
                            {
                                await _userManager.AddToRoleAsync(newUser, defaultRole);
                            }
                        }
                    }
                    else
                    {
                        // Do we need to update this user?
                    }
                }
            }
        }
    }
}