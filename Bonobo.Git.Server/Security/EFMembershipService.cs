﻿using System;
using System.Collections.Generic;
using System.Linq;
using Bonobo.Git.Server.Data;
using Bonobo.Git.Server.Models;
using System.Security.Cryptography;
using System.Data.Entity.Core;

namespace Bonobo.Git.Server.Security
{
    public class EFMembershipService : IMembershipService
    {
        private readonly Func<BonoboGitServerContext> _createDatabaseContext;
        private readonly IPasswordService _passwordService;

        public EFMembershipService() : this(() => new BonoboGitServerContext())
        {
        }

        public EFMembershipService(Func<BonoboGitServerContext> contextCreator)
        {
            // set up dependencies
            _createDatabaseContext = contextCreator;
            Action<string, string> updateUserPasswordHook =
                (username, password) =>
                {
                    using (var db = _createDatabaseContext())
                    {
                        var user = db.Users.FirstOrDefault(i => i.Username == username);
                        if (user != null)
                        {
                            UpdateUser(user.Id, null, null, null, null, password);
                        }
                    }
                };
            _passwordService = new PasswordService(updateUserPasswordHook);
        }

        public bool IsReadOnly()
        {
            return false;
        }

        public ValidationResult ValidateUser(string username, string password)
        {
            if (String.IsNullOrEmpty(username)) throw new ArgumentException("Value cannot be null or empty.", "username");
            if (String.IsNullOrEmpty(password)) throw new ArgumentException("Value cannot be null or empty.", "password");

            username = username.ToLowerInvariant();
            using (var database = _createDatabaseContext())
            {
                var user = database.Users.FirstOrDefault(i => i.Username == username);
                return user != null && _passwordService.ComparePassword(password, username, user.PasswordSalt, user.Password) ? ValidationResult.Success : ValidationResult.Failure;
            }
        }

        public bool CreateUser(string username, string password, string name, string surname, string email, Guid? guid)
        {
            if (String.IsNullOrEmpty(username)) throw new ArgumentException("Value cannot be null or empty.", "username");
            if (String.IsNullOrEmpty(password)) throw new ArgumentException("Value cannot be null or empty.", "password");
            if (String.IsNullOrEmpty(name)) throw new ArgumentException("Value cannot be null or empty.", "name");
            if (String.IsNullOrEmpty(surname)) throw new ArgumentException("Value cannot be null or empty.", "surname");
            if (String.IsNullOrEmpty(email)) throw new ArgumentException("Value cannot be null or empty.", "email");
            if ((!guid.HasValue) || guid.Value == Guid.Empty) guid = Guid.NewGuid();

            username = username.ToLowerInvariant();
            using (var database = _createDatabaseContext())
            {
                var user = new User
                {
                    Id = guid.Value,
                    Username = username,
                    Name = name,
                    Surname = surname,
                    Email = email,
                };
                SetPassword(user, password);
                database.Users.Add(user);
                try
                {
                    database.SaveChanges();
                }
                catch (UpdateException)
                {
                    return false;
                }
            }

            return true;
        }

        public IList<UserModel> GetAllUsers()
        {
            using (var db = _createDatabaseContext())
            {
                return db.Users.Include("Roles").ToList().Select(item => new UserModel
                {
                    Id = item.Id,
                    Name = item.Username,
                    GivenName = item.Name,
                    Surname = item.Surname,
                    Email = item.Email,
                }).ToList();
            }
        }

        public int UserCount()
        {
            using (var db = _createDatabaseContext())
            {
                return db.Users.Count();
            }
        }

        private UserModel GetUserModel(User user)
        {
            return user == null ? null : new UserModel
            {
                Id = user.Id,
                Name = user.Username,
                GivenName = user.Name,
                Surname = user.Surname,
                Email = user.Email,
             };
        }

        public UserModel GetUserModel(Guid id)
        {
            using (var db = _createDatabaseContext())
            {
                var user = db.Users.FirstOrDefault(i => i.Id == id);
                return GetUserModel(user);
            }
        }

        public UserModel GetUserModel(string username)
        {
            using (var db = _createDatabaseContext())
            {
                username = username.ToLowerInvariant();
                var user = db.Users.FirstOrDefault(i => i.Username == username);
                return GetUserModel(user);
            }
        }

        public void UpdateUser(Guid id, string username, string name, string surname, string email, string password)
        {
            using (var db = _createDatabaseContext())
            {
                var user = db.Users.FirstOrDefault(i => i.Id == id);
                if (user != null)
                {
                    var lowerUsername = username == null ? null : username.ToLowerInvariant();
                    user.Username = lowerUsername ?? user.Name;
                    user.Name = name ?? user.Name;
                    user.Surname = surname ?? user.Surname;
                    user.Email = email ?? user.Email;
                    if (password != null)
                    {
                        SetPassword(user, password);
                    }
                    db.SaveChanges();
                }
            }
        }

        public void DeleteUser(Guid id)
        {
            using (var db = _createDatabaseContext())
            {
                foreach (var user in db.Users)
                {
                    if (user.Id == id)
                    {
                        user.AdministratedRepositories.Clear();
                        user.Roles.Clear();
                        user.Repositories.Clear();
                        user.Teams.Clear();
                        db.Users.Remove(user);
                        db.SaveChanges();
                    }
                }
            }
        }

        private void SetPassword(User user, string password)
        {
            if (user == null) throw new ArgumentNullException("user", "User cannot be null");
            if (String.IsNullOrEmpty(password)) throw new ArgumentException("Password cannot be null or empty.", "password");

            user.PasswordSalt = Guid.NewGuid().ToString();
            user.Password = _passwordService.GetSaltedHash(password, user.PasswordSalt);
        }

        private const int PBKDF2IterCount = 1000; // default for Rfc2898DeriveBytes
        private const int PBKDF2SubkeyLength = 256 / 8; // 256 bits
        private const int SaltSize = 128 / 8; // 128 bits

        public string GenerateResetToken(string username)
        {
            byte[] salt;
            byte[] subkey;
            using (var deriveBytes = new Rfc2898DeriveBytes(username, SaltSize, PBKDF2IterCount))
            {
                salt = deriveBytes.Salt;
                subkey = deriveBytes.GetBytes(PBKDF2SubkeyLength);
            }

            byte[] outputBytes = new byte[1 + SaltSize + PBKDF2SubkeyLength];
            Buffer.BlockCopy(salt, 0, outputBytes, 1, SaltSize);
            Buffer.BlockCopy(subkey, 0, outputBytes, 1 + SaltSize, PBKDF2SubkeyLength);
            return Convert.ToBase64String(outputBytes);
        }        
    }
}