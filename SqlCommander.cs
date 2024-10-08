using System.Text;
using System.Net.WebSockets;
using Npgsql;
using System.Security.Cryptography;
using System.Data;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Specialized;
using System.Globalization;
using System.Numerics;
using System.Diagnostics;
using System.Net;
using System.ComponentModel;
using System;
using System.Collections.Generic;


namespace shooter_server
{
    public class SqlCommander
    {
        private string host;
        private string user;
        private string password;
        private string database;
        private int port;


        public SqlCommander(string host, string user, string password, string database, int port)
        {
            this.host = host;
            this.user = user;
            this.password = password;
            this.database = database;
            this.port = port;
        }


        public async Task ExecuteSqlCommand(Lobby lobby, WebSocket webSocket, string sqlCommand, Player player)
        {
            // Создание соединения с базой данных
            using (var dbConnection = new NpgsqlConnection($"Host={host};Username={user};Password={password};Database={database};Port={port}"))
            {
                await dbConnection.OpenAsync();
                //Console.WriteLine(dbConnection.ConnectionString);

                int senderId = player.Id;

                if (dbConnection.State != ConnectionState.Open)
                {
                    //Console.WriteLine("DB connection error");

                    return;
                }

                //Console.WriteLine(sqlCommand);

                try
                {
                    // Определение типа SQL-команды
                    switch (sqlCommand)
                    {

                        case string s when s.StartsWith("AddQueue"):
                            //RW
                            await Task.Run(() => AltSendMessage(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("addUserToChat"):
                            //RW
                            await Task.Run(() => addUserToChat(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("ChatCreate"):
                            //RW
                            await Task.Run(() => ChatCreate(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("DeleteChat"):
                            //RW
                            await Task.Run(() => DeleteChat(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("Login"):
                            //OK
                            await Task.Run(() => Login(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        case string s when s.StartsWith("AltRegister"):
                            //RW
                            await Task.Run(() => Register(sqlCommand, senderId, dbConnection, lobby, webSocket));
                            break;
                        default:
                            Console.WriteLine("Command not found");
                            break;
                    }
                }
                catch (Exception e)
                {
                    //Console.WriteLine($"Error executing SQL command: {e}");
                }
            }
        }


        // Удаление чата со всеми пользователями и всеми сообщениями
        private async Task DeleteChat(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                using (var cursor = dbConnection.CreateCommand())
                {
                    // DeleteChat requestId idChat
                    List<string> credentials = new List<string>(sqlCommand.Split(' '));

                    credentials.RemoveAt(0);

                    int requestId = int.Parse(credentials[0]);
                    string idChat = credentials[1];

                    cursor.CommandText = @"SELECT id_user FROM users WHERE id_chat = @idChat;";
                    cursor.Parameters.AddWithValue("idChat", idChat);

                    var reader = await cursor.ExecuteReaderAsync();

                    while (await reader.ReadAsync())
                    {
                        string idUser = reader.GetString(0);

                        using (var command = dbConnection.CreateCommand())
                        {
                            command.CommandText = @"DELETE FROM messages WHERE id_sender = @idUser;";
                            command.Parameters.AddWithValue("idUser", idUser);

                            await command.ExecuteNonQueryAsync();
                        }
                    }

                    reader.Close();

                    cursor.CommandText = @"DELETE FROM users WHERE id_chat = @idChat;";
                    cursor.Parameters.AddWithValue("idChat", idChat);

                    await cursor.ExecuteNonQueryAsync();

                    cursor.CommandText = @"DELETE FROM chat WHERE id_chat = @idChat;";
                    cursor.Parameters.AddWithValue("idChat", idChat);

                    await cursor.ExecuteNonQueryAsync();
                }
            }
            catch (Exception e)
            {
                //Console.WriteLine($"Error DeleteMessages command: {e}");
            }
        }

        private int GenerateUniqueUserId(NpgsqlConnection dbConnection)
        {
            try
            {
                const int max_user_id = int.MaxValue;
                Random random = new Random();

                int idSender;

                do
                {
                    idSender = random.Next(max_user_id);

                    using (var cursor = dbConnection.CreateCommand())
                    {
                        cursor.Parameters.AddWithValue("idSender", idSender);

                        cursor.CommandText = $"SELECT COUNT(*) FROM users WHERE id_user = @idSender";

                        long idUserCount = (long)cursor.ExecuteScalar();

                        if (idUserCount > 0)
                        {
                            idSender = -1;
                        }
                    }
                } while (idSender == -1);

                return (int)idSender;
            }
            catch (Exception e)
            {
                //Console.WriteLine($"Error GenerateUniqueUserId command: {e}");
                return -1;
            }
        }


        private string GenerateUniqueChatId(NpgsqlConnection dbConnection)
        {
            try
            {
                Random random = new Random();

                string idChat = "";

                do
                {
                    StringBuilder sb = new StringBuilder();

                    sb.Append(random.Next(9) + 1);

                    for (int i = 0; i < 127; ++i)
                    {
                        sb.Append(random.Next(10));
                    }

                    idChat = sb.ToString();

                    using (var cursor = dbConnection.CreateCommand())
                    {
                        //Console.WriteLine(idChat + " " + idChat.GetType());

                        cursor.Parameters.AddWithValue("idChat", idChat);

                        cursor.CommandText = $"SELECT COUNT(*) FROM chat WHERE id_chat = @idChat";

                        long idChatCount = (long)cursor.ExecuteScalar();

                        if (idChatCount > 0)
                        {
                            idChat = "";
                        }
                    }
                } while (idChat == "");

                return idChat;
            }
            catch (Exception e)
            {
                //Console.WriteLine($"Error GenerateUniqueUserId command: {e}");
                return "";
            }
        }


        private int GenerateUniqueUserIdAccount(NpgsqlConnection dbConnection)
        {
            int maxId = -1;

            try
            {
                using (var cursor = dbConnection.CreateCommand())
                {
                    cursor.CommandText = $"SELECT COALESCE(MAX(id_user), 0) FROM user_account";

                    var result = cursor.ExecuteScalar();

                    if (result != null && result != DBNull.Value)
                    {
                        maxId = (int)result;
                    }
                }
            }
            catch (Exception e)
            {
                //Console.WriteLine($"Error GenerateUniqueUserId command: {e}");
                return -1;
            }

            return maxId == -1 ? -1 : maxId + 1;
        }


        // Создание нового чата 
        private async Task ChatCreate(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                using (var cursor = dbConnection.CreateCommand())
                {
                    // ChatCreate requestId isPrivacy chatPassword
                    List<string> credentials = new List<string>(sqlCommand.Split(' '));

                    credentials.RemoveAt(0);
                    //Console.WriteLine("\n\n" + "YAAAAAAAAAAAAAAAAAAAAAAA NE JIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIIV" + "\n\n");

                    int requestId = int.Parse(credentials[0]);
                    bool isPrivacy = bool.Parse(credentials[1]);
                    string chatPassword = credentials.Count == 3 ? credentials[2] : "";

                    string idChat = GenerateUniqueChatId(dbConnection);

                    cursor.CommandText = @"INSERT INTO chat (id_chat, chat_password, is_privacy) VALUES (@idChat, @chatPassword, @isPrivacy);";
                    cursor.Parameters.AddWithValue("idChat", idChat);
                    cursor.Parameters.AddWithValue("chatPassword", chatPassword);
                    cursor.Parameters.AddWithValue("isPrivacy", isPrivacy);

                    await cursor.ExecuteNonQueryAsync();

                    //Console.WriteLine("Chat Created");

                    lobby.SendMessagePlayer(idChat, ws, requestId);
                }
            }
            catch (Exception e)
            {
                //Console.WriteLine($"Error ChatCreate command: {e}");
            }
        }


        // Добавить пользователя в чат
        private async Task addUserToChat(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                using (var cursor = dbConnection.CreateCommand())
                {
                    //Console.WriteLine(sqlCommand);

                    int idUser = GenerateUniqueUserId(dbConnection);

                    // addUserToChat requestId publicKey idChat chatPassword 
                    List<string> credentials = new List<string>(sqlCommand.Split(' '));
                    credentials.RemoveAt(0);

                    int requestId = int.Parse(credentials[0]);
                    credentials.RemoveAt(0);

                    byte[] publicKey = Convert.FromBase64String(credentials[0]);
                    credentials.RemoveAt(0);

                    if (credentials.Count == 2 || credentials.Count == 1)
                    {
                        // Если чат с паролем
                        string idChat = credentials[0];
                        string chatPassword = credentials.Count == 2 ? credentials[1] : "";

                        cursor.Parameters.AddWithValue("idChat", idChat);
                        cursor.Parameters.AddWithValue("chatPassword", chatPassword);

                        cursor.CommandText = @"SELECT id_chat FROM chat WHERE id_chat = @idChat" +
                            (chatPassword != "" ? " AND chat_password = @chatPassword" : "") + ";";

                        //Console.WriteLine("\n\n\n" + chatPassword + "\n" + idChat + "\n\n\n");

                        using (var reader = cursor.ExecuteReader())
                        {
                            if (await reader.ReadAsync())
                            {
                                reader.Close();

                                cursor.Parameters.AddWithValue("idUser", idUser);
                                cursor.Parameters.AddWithValue("idChat", idChat);
                                cursor.Parameters.AddWithValue("publicKey", publicKey);

                                cursor.CommandText = @"INSERT INTO users (id_user, id_chat, public_key) VALUES (@idUser, @idChat, @publicKey);";

                                await cursor.ExecuteNonQueryAsync();

                                //Console.WriteLine($"Success");

                                lobby.SendMessagePlayer(idUser.ToString(), ws, requestId);
                            }
                            else
                            {
                                //Console.WriteLine("No matching records found.");
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                //Console.WriteLine($"Error addUserToChat command: {e}");
            }
        }

        private async Task<(int, string)> CreateGetMessagesStrAsync(NpgsqlConnection dbConnection, List<string> credentials, long kChats, int startIndex)
        {
            StringBuilder str = new StringBuilder();
            int index = startIndex;
            int returnChatsCount = 0;

            for (int k = 0; k < kChats; k++)
            {
                string chatId = credentials[index++];
                long kSender = long.Parse(credentials[index++]);

                var (adI, missMsg) = await GetMessagesForAuthorsAsync(dbConnection, chatId, credentials, kSender, index);
                index = adI;
                if (missMsg == null || missMsg.Count == 0)
                {
                    continue;
                }

                returnChatsCount++;
                str.Append($" {chatId} {missMsg.Count}");

                foreach (var entry in missMsg)
                {
                    var (authorId, publicKey) = entry.Key;
                    var messages = entry.Value;
                    int messageCount = messages.Count;

                    if (publicKey == Array.Empty<byte>())
                    {
                        str.Append($" {authorId} false {messageCount}");
                    }
                    else
                    {
                        str.Append($" {authorId} true {Convert.ToBase64String(publicKey)} {messageCount}");
                    }

                    foreach (var msg in messages)
                    {
                        if (msg.is_erase)
                        {
                            str.Append($" {msg.id_msg} {msg.is_erase}");
                        }
                        else
                        {
                            str.Append($" {msg.id_msg} {msg.is_erase} {msg.time_msg.ToString("dd.MM.yyyy HH:mm:ss")} {Convert.ToBase64String(msg.msg)}");
                        }
                    }
                }
            }

            return (index, returnChatsCount > 0 ? $"{returnChatsCount}{str}" : "");
        }


        private async Task<(int, Dictionary<(int, byte[]), List<Message>>)> GetMessagesForAuthorsAsync(NpgsqlConnection dbConnection, string chatId, List<string> credentials, long kSender, int startIndex)
        {
            Dictionary<(int, byte[]), List<Message>> messagesByAuthors = new Dictionary<(int, byte[]), List<Message>>();
            int index = startIndex;

            List<int> idUsers = new List<int>();

            using (var cursor = dbConnection.CreateCommand())
            {
                cursor.Parameters.AddWithValue("chatId", chatId);

                cursor.CommandText = @"SELECT id_user FROM users WHERE id_chat = @chatId";

                using (var reader = await cursor.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        idUsers.Add(reader.GetInt32(0));
                    }
                }
            }

            for (int i = 0; i < kSender; i++)
            {
                if (!int.TryParse(credentials[index++], out int authorId))
                {
                    //Console.WriteLine($"Invalid authorId at index {index - 1}");
                    continue;
                }

                idUsers.Remove(authorId);

                if (!bool.TryParse(credentials[index++], out bool authorKey))
                {
                    //Console.WriteLine($"Invalid authorId at index {index - 1}");
                    continue;
                }

                if (!int.TryParse(credentials[index++], out int kMsg))
                {
                    //Console.WriteLine($"Invalid kMsg at index {index - 1}");
                    continue;
                }

                byte[] publicKey = Array.Empty<byte>();

                if (!authorKey)
                {
                    using (var cursor = dbConnection.CreateCommand())
                    {
                        cursor.Parameters.AddWithValue("authorId", authorId);

                        cursor.CommandText = @"SELECT public_key FROM users WHERE id_user = @authorId";

                        using (var reader = await cursor.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                publicKey = reader.GetFieldValue<byte[]>(0);
                            }
                        }
                    }
                }

                List<int> messageIds = new List<int>();
                for (int j = 0; j < kMsg; j++)
                {
                    if (!int.TryParse(credentials[index++], out int messageId))
                    {
                        //Console.WriteLine($"Invalid messageId at index {index - 1}");
                        continue;
                    }
                    messageIds.Add(messageId);
                }

                using (var command = dbConnection.CreateCommand())
                {
                    command.CommandText = @"
                SELECT m.id_msg, m.time_msg, m.msg, m.is_erase
                FROM messages m
                JOIN users u ON m.id_sender = u.id_user
                WHERE u.id_chat = @chatId 
                  AND m.id_sender = @authorId
                  AND ((m.id_msg = ANY(@messageIds) OR m.id_msg > @lastMsgId) OR (m.is_erase = true))
                ORDER BY m.id_msg";
                    command.Parameters.AddWithValue("@chatId", chatId);
                    command.Parameters.AddWithValue("@authorId", authorId);
                    command.Parameters.AddWithValue("@messageIds", messageIds.ToArray());
                    command.Parameters.AddWithValue("@lastMsgId", messageIds.Last());

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            int messageId = reader.GetInt32(0);
                            DateTime timeMsg = reader.GetDateTime(1);
                            byte[] msg = reader.GetFieldValue<byte[]>(2);
                            bool is_erase = reader.GetBoolean(3);

                            if (!messagesByAuthors.ContainsKey((authorId, publicKey)))
                            {
                                messagesByAuthors[(authorId, publicKey)] = new List<Message>();
                            }

                            messagesByAuthors[(authorId, publicKey)].Add(new Message
                            {
                                id_msg = messageId,
                                time_msg = timeMsg,
                                msg = msg,
                                is_erase = is_erase
                            });
                        }
                    }
                }
            }

            for (int i = 0; i < idUsers.Count; i++)
            {
                bool authorKey = false;

                byte[] publicKey = Array.Empty<byte>();

                if (!authorKey)
                {
                    using (var cursor = dbConnection.CreateCommand())
                    {
                        cursor.Parameters.AddWithValue("authorId", idUsers[i]);

                        cursor.CommandText = @"SELECT public_key FROM users WHERE id_user = @authorId";

                        using (var reader = await cursor.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                publicKey = reader.GetFieldValue<byte[]>(0);
                            }
                        }
                    }
                }

                int messageIds = 0;

                using (var command = dbConnection.CreateCommand())
                {
                    command.CommandText = @"
                SELECT m.id_msg, m.time_msg, m.msg, m.is_erase
                FROM messages m
                JOIN users u ON m.id_sender = u.id_user
                WHERE u.id_chat = @chatId 
                  AND m.id_sender = @authorId
                  AND ((m.id_msg >= @messageIds) OR (m.is_erase = true))
                ORDER BY m.id_msg";
                    command.Parameters.AddWithValue("@chatId", chatId);
                    command.Parameters.AddWithValue("@authorId", idUsers[i]);
                    command.Parameters.AddWithValue("@messageIds", messageIds);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            int messageId = reader.GetInt32(0);
                            DateTime timeMsg = reader.GetDateTime(1);
                            byte[] msg = reader.GetFieldValue<byte[]>(2);
                            bool is_erase = reader.GetBoolean(3);

                            if (!messagesByAuthors.ContainsKey((idUsers[i], publicKey)))
                            {
                                messagesByAuthors[(idUsers[i], publicKey)] = new List<Message>();
                            }

                            messagesByAuthors[(idUsers[i], publicKey)].Add(new Message
                            {
                                id_msg = messageId,
                                time_msg = timeMsg,
                                msg = msg,
                                is_erase = is_erase
                            });
                        }
                    }
                }
            }

            return (index, messagesByAuthors);
        }

        private async Task AltSendMessage(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                // SendMessage requestId id_sender time_msg msg
                List<string> credentials = new List<string>(sqlCommand.Split(' '));

                credentials.RemoveAt(0);

                int requestId = int.Parse(credentials[0]);
                int idSender = int.Parse(credentials[1]);

                string time1 = credentials[2];
                string time2 = credentials[3];
                string time = time1 + " " + time2;
                string format = "yyyy-MM-dd HH:mm:ss";
                CultureInfo provider = CultureInfo.InvariantCulture;
                DateTimeOffset timeMsg = DateTimeOffset.ParseExact(time, format, provider);

                byte[] msg = new byte[0];
                try
                {
                    msg = Convert.FromBase64String(credentials[4]);
                }
                catch (FormatException ex)
                {
                    // Handle the format exception (e.g., invalid Base64 string)
                    //Console.WriteLine("Error decoding Base64 string: " + ex.Message);
                }

                string idChat = "";
                using (var cursor = dbConnection.CreateCommand())
                {
                    cursor.Parameters.AddWithValue("idSender", idSender);
                    cursor.CommandText = "SELECT id_chat FROM users WHERE id_user = @idSender";

                    object result = await cursor.ExecuteScalarAsync();
                    if (result != null)
                    {
                        idChat = (string)result;
                    }
                }

                long idMsg;

                using (var cursor = dbConnection.CreateCommand())
                {
                    cursor.Parameters.AddWithValue("idSender", idSender);
                    cursor.CommandText = "SELECT MAX(changeid) FROM chatqueue WHERE user_id = @idSender;";

                    object result = await cursor.ExecuteScalarAsync();

                    if (result != DBNull.Value)
                    {
                        idMsg = (long)result;
                    }
                    else
                    {
                        idMsg = 0;
                    }
                }


                using (var cursor = dbConnection.CreateCommand())
                {
                    cursor.CommandText = "INSERT INTO chatqueue (chatid, changeid, changedata, user_id) VALUES (@idChat, @idMsg, @msg, @idSender)";
                    // Добавление параметров в команду для предотвращения SQL-инъекций
                    cursor.Parameters.AddWithValue("idSender", idSender);
                    cursor.Parameters.AddWithValue("idMsg", idMsg + 1);
                    cursor.Parameters.AddWithValue("idChat", idChat);
                    cursor.Parameters.AddWithValue("msg", msg);

                    await cursor.ExecuteNonQueryAsync();

                    lobby.SendMessagePlayer($"true", ws, requestId);
                }
            }
            catch (Exception e)
            {
                //Console.WriteLine($"Error SendMessage command: {e}");
            }
        }


        private async Task Login(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                // Извлекаем параметры запроса: requestId, login и password
                List<string> credentials = new List<string>(sqlCommand.Split(' '));
                credentials.RemoveAt(0); // Убираем саму команду из запроса

                int requestId = int.Parse(credentials[0]);
                byte[] login = Convert.FromBase64String(credentials[1]);
                byte[] password = Convert.FromBase64String(credentials[2]);

                // Выполняем запрос для получения AnonId по логину и паролю
                using (var cursor = dbConnection.CreateCommand())
                {
                    cursor.CommandText = @"
                        SELECT 
                            a.AnonId 
                        FROM 
                            Anon a
                        WHERE 
                            a.Login = @login AND a.Password = @password";

                    // Добавляем параметры запроса
                    cursor.Parameters.AddWithValue("login", login);
                    cursor.Parameters.AddWithValue("password", password);

                    // Выполняем запрос и читаем результат
                    using (var reader = await cursor.ExecuteReaderAsync())
                    {
                        if (reader.Read())
                        {
                            int anonId = reader.GetInt32(0);

                            // Отправляем AnonId в ответ на успешный логин
                            string result = $"true {anonId}";
                            lobby.SendMessagePlayer(result, ws, requestId);
                        }
                        else
                        {
                            // Если логин или пароль неверны
                            string result = "false Invalid login or password";
                            lobby.SendMessagePlayer(result, ws, requestId);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error SendMessage command: {e}");
            }
        }



        private async Task Register(string sqlCommand, int senderId, NpgsqlConnection dbConnection, Lobby lobby, WebSocket ws)
        {
            try
            {
                // SendMessage requestId login password
                List<string> credentials = new List<string>(sqlCommand.Split(' '));

                credentials.RemoveAt(0);

                int requestId = int.Parse(credentials[0]);
                byte[] login = Convert.FromBase64String(credentials[1]);
                byte[] password = Convert.FromBase64String(credentials[2]);

                // Check if the login already exists
                using (var checkCmd = dbConnection.CreateCommand())
                {
                    checkCmd.CommandText = "SELECT COUNT(*) FROM Anon WHERE Login = @login";
                    checkCmd.Parameters.AddWithValue("login", login);

                    int count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());

                    if (count > 0)
                    {
                        // Login already exists
                        lobby.SendMessagePlayer($"User with this login already exists", ws, requestId);
                    }
                    else
                    {
                        // Insert new record
                        using (var insertCmd = dbConnection.CreateCommand())
                        {
                            insertCmd.CommandText = "INSERT INTO Anon (Login, Password) VALUES (@login, @password)";
                            insertCmd.Parameters.AddWithValue("login", login);
                            insertCmd.Parameters.AddWithValue("password", password);

                            await insertCmd.ExecuteNonQueryAsync();

                            // Registration successful
                            lobby.SendMessagePlayer($"true", ws, requestId);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error SendMessage command: {e}");
            }
        }

    }
}
