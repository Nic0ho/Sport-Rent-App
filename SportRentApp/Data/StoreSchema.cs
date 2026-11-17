using System;
using System.Configuration;
using System.Data.SQLite;
using System.IO;

namespace SportRentApp
{
    internal static class StoreSchema
    {
        public static string ConnectionString
        {
            get
            {
                var builder = new SQLiteConnectionStringBuilder(ConfigurationManager.ConnectionStrings["DefaultConnection"].ConnectionString);
                if (!Path.IsPathRooted(builder.DataSource))
                    builder.DataSource = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, builder.DataSource));
                builder.ForeignKeys = true;
                return builder.ConnectionString;
            }
        }

        public static void Ensure()
        {
            var path = new SQLiteConnectionStringBuilder(ConnectionString).DataSource;
            bool newFile = !File.Exists(path);
            using (var db = new SQLiteConnection(ConnectionString))
            {
                db.Open();
                int schemaVersion;
                using (var version = new SQLiteCommand("PRAGMA user_version", db))
                    schemaVersion = Convert.ToInt32(version.ExecuteScalar());
                if (schemaVersion >= 9)
                    return;
                if (!newFile)
                {
                    string backup = path + ".before-schema-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N") + ".bak";
                    using (var copy = new SQLiteConnection("Data Source=" + backup))
                    {
                        copy.Open();
                        db.BackupDatabase(copy, "main", "main", -1, null, 0);
                    }
                }

                if (schemaVersion == 8)
                {
                    using (var tx = db.BeginTransaction())
                    {
                        NumberShops(db, tx);
                        tx.Commit();
                    }

                    return;
                }

                if (schemaVersion == 7)
                {
                    using (var tx = db.BeginTransaction())
                    {
                        UnifyWorkplaces(db, tx);
                        tx.Commit();
                    }

                    return;
                }

                if (schemaVersion == 6)
                {
                    using (var tx = db.BeginTransaction())
                    {
                        AddJournal(db, tx);
                        tx.Commit();
                    }

                    return;
                }

                if (schemaVersion == 5)
                {
                    using (var tx = db.BeginTransaction())
                    {
                        EnableClientRegistration(db, tx);
                        tx.Commit();
                    }

                    return;
                }

                if (schemaVersion == 4)
                {
                    using (var tx = db.BeginTransaction())
                    {
                        MergeShiftLogs(db, tx);
                        tx.Commit();
                    }

                    return;
                }

                if (schemaVersion == 3)
                {
                    using (var tx = db.BeginTransaction())
                    {
                        AddTeamSchema(db, tx);
                        tx.Commit();
                    }

                    return;
                }

                if (schemaVersion == 2)
                {
                    using (var tx = db.BeginTransaction())
                    {
                        CleanClientTags(db, tx);
                        AddTeamSchema(db, tx);
                        tx.Commit();
                    }

                    return;
                }

                using (var keys = new SQLiteCommand("PRAGMA foreign_keys=OFF", db))
                    keys.ExecuteNonQuery();
                if (newFile)
                {
                    string[] baseTables =
                    {
                        "CREATE TABLE Users (Id INTEGER PRIMARY KEY AUTOINCREMENT, Username TEXT NOT NULL UNIQUE, Password TEXT NOT NULL, FullName TEXT NOT NULL, Phone TEXT, Role TEXT NOT NULL, Notes TEXT, CreatedAt TEXT)",
                        "CREATE TABLE Equipment (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, Category TEXT NOT NULL, Description TEXT, PricePerDay REAL NOT NULL DEFAULT 0, Status TEXT NOT NULL DEFAULT 'Available', ImagePath TEXT)",
                        "CREATE TABLE Rentals (Id INTEGER PRIMARY KEY AUTOINCREMENT, OrderNumber TEXT, PickupCode TEXT, UserId INTEGER NOT NULL REFERENCES Users(Id), EquipmentId INTEGER NOT NULL REFERENCES Equipment(Id), QueueType TEXT, OperationType TEXT, RentDate TEXT NOT NULL, ReturnDate TEXT NOT NULL, ActualReturnDate TEXT, RentalPrice REAL DEFAULT 0, Deposit REAL DEFAULT 0, PenaltyFee REAL DEFAULT 0, TotalPrice REAL DEFAULT 0, Status TEXT NOT NULL DEFAULT 'Pending', ConditionNotes TEXT, RequiresAdmin INTEGER DEFAULT 0, AdminNotes TEXT, CreatedAt TEXT)"
                    };
                    foreach (var statement in baseTables)
                        using (var command = new SQLiteCommand(statement, db))
                            command.ExecuteNonQuery();
                }

                bool migrate;
                using (var check = new SQLiteCommand("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='AccountProfiles'", db))
                    migrate = Convert.ToInt32(check.ExecuteScalar()) == 0;
                using (var tx = db.BeginTransaction())
                {
                    string[] sql =
                    {
                        "CREATE TABLE IF NOT EXISTS AccountProfiles (UserId INTEGER PRIMARY KEY REFERENCES Users(Id), Status TEXT NOT NULL DEFAULT 'Pending', Tags TEXT NOT NULL DEFAULT '', ShopCode TEXT, Version INTEGER NOT NULL DEFAULT 0, ApprovedBy INTEGER REFERENCES Users(Id), UpdatedAt TEXT)",
                        "INSERT INTO AccountProfiles(UserId,Status,Tags,UpdatedAt) SELECT Id,'Active','',datetime('now') FROM Users WHERE Id NOT IN (SELECT UserId FROM AccountProfiles)",
                        "CREATE TABLE IF NOT EXISTS EquipmentRequests (Id INTEGER PRIMARY KEY AUTOINCREMENT, RequesterId INTEGER NOT NULL REFERENCES Users(Id), EquipmentId INTEGER NOT NULL REFERENCES Equipment(Id), Message TEXT NOT NULL, Status TEXT NOT NULL, Resolution TEXT, ReviewedBy INTEGER REFERENCES Users(Id), Version INTEGER NOT NULL DEFAULT 0, CreatedAt TEXT NOT NULL)",
                        "CREATE TABLE IF NOT EXISTS KioskVisits (Id INTEGER PRIMARY KEY AUTOINCREMENT, KioskId INTEGER NOT NULL REFERENCES Users(Id), ShopCode TEXT NOT NULL, ClientId INTEGER REFERENCES Users(Id), Phone TEXT NOT NULL, RequestKey TEXT NOT NULL UNIQUE, CreatedAt TEXT NOT NULL, Status TEXT NOT NULL)",
                        "CREATE TABLE IF NOT EXISTS ShopTransactions (Id INTEGER PRIMARY KEY AUTOINCREMENT, ClientId INTEGER NOT NULL REFERENCES Users(Id), OrderId INTEGER REFERENCES Rentals(Id), VisitId INTEGER REFERENCES KioskVisits(Id), RelatedTransactionId INTEGER REFERENCES ShopTransactions(Id), Kind TEXT NOT NULL, AmountMinor INTEGER NOT NULL CHECK(AmountMinor>0), Notes TEXT, IsDemo INTEGER NOT NULL DEFAULT 1 CHECK(IsDemo=1), CreatedBy INTEGER NOT NULL REFERENCES Users(Id), CreatedAt TEXT NOT NULL, RequestKey TEXT NOT NULL UNIQUE)",
                        "CREATE INDEX IF NOT EXISTS IX_Transactions_Client ON ShopTransactions(ClientId)",
                        "CREATE INDEX IF NOT EXISTS IX_Requests_Status ON EquipmentRequests(Status)",
                        "CREATE INDEX IF NOT EXISTS IX_Profiles_Status ON AccountProfiles(Status)",
                        "CREATE INDEX IF NOT EXISTS IX_Transactions_Parent ON ShopTransactions(RelatedTransactionId)",
                        "CREATE INDEX IF NOT EXISTS IX_Transactions_Order ON ShopTransactions(OrderId)",
                        "CREATE INDEX IF NOT EXISTS IX_Requests_Requester ON EquipmentRequests(RequesterId)"
                    };
                    foreach (var statement in sql)
                    {
                        if (!migrate && statement.StartsWith("INSERT INTO AccountProfiles", StringComparison.Ordinal))
                            continue;
                        using (var command = new SQLiteCommand(statement, db, tx))
                            command.ExecuteNonQuery();
                    }

                    AddColumn(db, tx, "AccountProfiles", "ShopCode", "TEXT");
                    AddColumn(db, tx, "KioskVisits", "ShopCode", "TEXT");
                    AddColumn(db, tx, "KioskVisits", "ClientId", "INTEGER REFERENCES Users(Id)");
                    using (var count = new SQLiteCommand("SELECT COUNT(*) FROM Users u JOIN AccountProfiles p ON p.UserId=u.Id WHERE u.Role='Admin' AND p.Status='Active'", db, tx))
                        if (Convert.ToInt32(count.ExecuteScalar()) == 0)
                        {
                            using (var insert = new SQLiteCommand("INSERT OR IGNORE INTO Users(Username,Password,FullName,Role,CreatedAt) VALUES('__setup__',@password,'Початкове налаштування','Bootstrap',datetime('now'))", db, tx))
                            {
                                insert.Parameters.AddWithValue("@password", PasswordHash.Create(Guid.NewGuid().ToString("N")));
                                insert.ExecuteNonQuery();
                            }

                            using (var insert = new SQLiteCommand("INSERT OR IGNORE INTO AccountProfiles(UserId,Status,Tags) SELECT Id,'Active','' FROM Users WHERE Role='Bootstrap'", db, tx))
                                insert.ExecuteNonQuery();
                        }

                    Consolidate(db, tx);
                    using (var check = new SQLiteCommand("PRAGMA foreign_key_check", db, tx))
                    using (var reader = check.ExecuteReader())
                        if (reader.Read())
                            throw new InvalidOperationException("Порушено зв’язки даних. Оновлення БД скасовано; резервну копію збережено.");
                    CleanClientTags(db, tx);
                    AddTeamSchema(db, tx);
                    tx.Commit();
                }
            }
        }

        private static void AddTeamSchema(SQLiteConnection db, SQLiteTransaction tx)
        {
            AddColumn(db, tx, "Users", "Bio", "TEXT");
            AddColumn(db, tx, "Users", "PhotoPath", "TEXT");
            AddColumn(db, tx, "Users", "Station", "TEXT NOT NULL DEFAULT ''");
            AddColumn(db, tx, "Users", "IsOnShift", "INTEGER NOT NULL DEFAULT 1");
            string[] sql =
            {
                "CREATE TABLE IF NOT EXISTS WorkTasks (Id INTEGER PRIMARY KEY AUTOINCREMENT, SourceKey TEXT NOT NULL UNIQUE, Kind TEXT NOT NULL, EntityId INTEGER, Title TEXT NOT NULL, Description TEXT, Station TEXT NOT NULL, Status TEXT NOT NULL DEFAULT 'Open', CreatedBy INTEGER REFERENCES Users(Id), CreatedAt TEXT NOT NULL)",
                "CREATE TABLE IF NOT EXISTS TaskAssignments (Id INTEGER PRIMARY KEY AUTOINCREMENT, TaskId INTEGER NOT NULL REFERENCES WorkTasks(Id), UserId INTEGER NOT NULL REFERENCES Users(Id), RequestedBy INTEGER REFERENCES Users(Id), Status TEXT NOT NULL, UNIQUE(TaskId,UserId))",
                "CREATE TABLE IF NOT EXISTS LocalMessages (Id INTEGER PRIMARY KEY AUTOINCREMENT, SenderId INTEGER NOT NULL REFERENCES Users(Id), RecipientId INTEGER REFERENCES Users(Id), Body TEXT NOT NULL, CreatedAt TEXT NOT NULL, Archived INTEGER NOT NULL DEFAULT 0)",
                "CREATE INDEX IF NOT EXISTS IX_Messages_Conversation ON LocalMessages(SenderId,RecipientId,Id)",
                "CREATE INDEX IF NOT EXISTS IX_Assignments_User ON TaskAssignments(UserId,Status)",
                "UPDATE Users SET Station=CASE WHEN instr(Tags,'Каса')>0 THEN 'Каса' WHEN instr(Tags,'Склад')>0 THEN 'Склад' ELSE '' END WHERE Role='Employee'",
                "PRAGMA user_version=5"
            };
            foreach (var statement in sql)
                using (var command = new SQLiteCommand(statement, db, tx))
                    command.ExecuteNonQuery();
            EnableClientRegistration(db, tx);
        }

        private static void MergeShiftLogs(SQLiteConnection db, SQLiteTransaction tx)
        {
            string[] sql =
            {
                "CREATE TABLE LocalMessages_new (Id INTEGER PRIMARY KEY AUTOINCREMENT, SenderId INTEGER NOT NULL REFERENCES Users(Id), RecipientId INTEGER REFERENCES Users(Id), Body TEXT NOT NULL, CreatedAt TEXT NOT NULL, Archived INTEGER NOT NULL DEFAULT 0)",
                "INSERT INTO LocalMessages_new SELECT Id,SenderId,RecipientId,Body,CreatedAt,Archived FROM LocalMessages",
                "INSERT INTO LocalMessages_new(SenderId,RecipientId,Body,CreatedAt,Archived) SELECT UserId,NULL,Summary,CreatedAt,1 FROM ShiftLogs ORDER BY Id",
                "DROP TABLE LocalMessages",
                "ALTER TABLE LocalMessages_new RENAME TO LocalMessages",
                "DROP TABLE ShiftLogs",
                "CREATE INDEX IX_Messages_Conversation ON LocalMessages(SenderId,RecipientId,Id)",
                "PRAGMA user_version=5"
            };
            foreach (var statement in sql)
                using (var command = new SQLiteCommand(statement, db, tx))
                    command.ExecuteNonQuery();
            using (var command = new SQLiteCommand("PRAGMA foreign_key_check", db, tx))
            using (var reader = command.ExecuteReader())
                if (reader.Read())
                    throw new InvalidOperationException("Не вдалося зберегти зв’язки повідомлень. Міграцію скасовано.");
            EnableClientRegistration(db, tx);
        }

        private static void EnableClientRegistration(SQLiteConnection db, SQLiteTransaction tx)
        {
            string[] sql =
            {
                "UPDATE Users SET Status=CASE WHEN instr(','||replace(Tags,' ','')||',',',Бан,')>0 THEN 'Blocked' ELSE 'Active' END,Version=Version+1,UpdatedAt=datetime('now') WHERE Role='Client' AND Status='Pending'",
                "UPDATE WorkTasks SET Status='Done' WHERE Kind='Account' AND Status='Open' AND EntityId IN (SELECT Id FROM Users WHERE Role='Client')",
                "PRAGMA user_version=6"
            };
            foreach (var statement in sql)
                using (var command = new SQLiteCommand(statement, db, tx))
                    command.ExecuteNonQuery();
            AddJournal(db, tx);
        }

        private static void AddJournal(SQLiteConnection db, SQLiteTransaction tx)
        {
            using (var command = new SQLiteCommand("CREATE TABLE IF NOT EXISTS ShopAudit (Id INTEGER PRIMARY KEY AUTOINCREMENT, ActorId INTEGER REFERENCES Users(Id), Action TEXT NOT NULL, Target TEXT, Details TEXT, CreatedAt TEXT NOT NULL)", db, tx))
                command.ExecuteNonQuery();
            AddColumn(db, tx, "ShopAudit", "ActorId", "INTEGER REFERENCES Users(Id)");
            AddColumn(db, tx, "ShopAudit", "Target", "TEXT");
            AddColumn(db, tx, "ShopAudit", "Details", "TEXT");
            AddColumn(db, tx, "ShopAudit", "CreatedAt", "TEXT");
            AddColumn(db, tx, "TaskAssignments", "IsMentioned", "INTEGER NOT NULL DEFAULT 0");
            string[] sql =
            {
                "INSERT INTO ShopAudit(ActorId,Action,Target,Details,CreatedAt) SELECT SenderId,'Зміну завершено','Зміна',Body,CreatedAt FROM LocalMessages WHERE RecipientId IS NULL",
                "DELETE FROM LocalMessages WHERE RecipientId IS NULL",
                "CREATE INDEX IF NOT EXISTS IX_Audit_Date ON ShopAudit(CreatedAt)",
                "PRAGMA user_version=7"
            };
            foreach (var statement in sql)
                using (var command = new SQLiteCommand(statement, db, tx))
                    command.ExecuteNonQuery();
            UnifyWorkplaces(db, tx);
        }

        private static void UnifyWorkplaces(SQLiteConnection db, SQLiteTransaction tx)
        {
            bool hasStation = false;
            using (var command = new SQLiteCommand("PRAGMA table_info(Users)", db, tx))
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                    if (reader["name"].ToString() == "Station")
                        hasStation = true;
            if (hasStation)
            {
                foreach (string place in StaffWork.Places)
                    using (var command = new SQLiteCommand("UPDATE Users SET Tags=CASE WHEN trim(Tags)='' THEN @place ELSE Tags||', '||@place END WHERE Role IN ('Admin','Employee') AND instr(','||replace(Station,' ','')||',',','||@place||',')>0 AND instr(','||replace(Tags,' ','')||',',','||@place||',')=0", db, tx))
                    {
                        command.Parameters.AddWithValue("@place", place);
                        command.ExecuteNonQuery();
                    }

                using (var command = new SQLiteCommand("ALTER TABLE Users DROP COLUMN Station", db, tx))
                    command.ExecuteNonQuery();
            }

            using (var command = new SQLiteCommand("PRAGMA user_version=8", db, tx))
                command.ExecuteNonQuery();
            NumberShops(db, tx);
        }

        private static void NumberShops(SQLiteConnection db, SQLiteTransaction tx)
        {
            var codes = new System.Collections.Generic.List<string>();
            using (var cmd = new SQLiteCommand("SELECT ShopCode FROM Users UNION SELECT ShopCode FROM Rentals ORDER BY 1", db, tx))
            using (var rows = cmd.ExecuteReader())
                while (rows.Read())
                    if (!rows.IsDBNull(0))
                        codes.Add(rows.GetString(0));
            var used = new System.Collections.Generic.HashSet<int>();
            foreach (string code in codes)
                if (InputRules.StoreNumber(code, out int number))
                    used.Add(number);
            int next = 1;
            foreach (string code in codes)
            {
                if (string.IsNullOrWhiteSpace(code))
                    continue;
                if (!InputRules.StoreNumber(code, out int number))
                {
                    while (used.Contains(next))
                        next++;
                    number = next++;
                    used.Add(number);
                }

                string value = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (value == code)
                    continue;
                foreach (string table in new[] { "Users", "Rentals" })
                    using (var cmd = new SQLiteCommand("UPDATE " + table + " SET ShopCode=@number WHERE ShopCode=@old", db, tx))
                    {
                        cmd.Parameters.AddWithValue("@number", value);
                        cmd.Parameters.AddWithValue("@old", code);
                        cmd.ExecuteNonQuery();
                    }
            }

            using (var cmd = new SQLiteCommand("PRAGMA user_version=9", db, tx))
                cmd.ExecuteNonQuery();
        }

        private static void CleanClientTags(SQLiteConnection db, SQLiteTransaction tx)
        {
            var changes = new System.Collections.Generic.List<Tuple<int, string>>();
            using (var command = new SQLiteCommand("SELECT Id,Tags,Status FROM Users WHERE Role='Client'", db, tx))
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                {
                    var tags = System.Linq.Enumerable.Where(reader["Tags"].ToString().Split(','), tag => Array.IndexOf(ShopService.ClientTagNames, tag.Trim()) >= 0);
                    string value = string.Join(", ", System.Linq.Enumerable.Distinct(System.Linq.Enumerable.Select(tags, tag => tag.Trim())));
                    if (reader["Status"].ToString() == "Blocked" && !value.Contains("Бан"))
                        value += (value.Length == 0 ? "" : ", ") + "Бан";
                    changes.Add(Tuple.Create(Convert.ToInt32(reader["Id"]), value));
                }

            foreach (var change in changes)
                using (var command = new SQLiteCommand("UPDATE Users SET Tags=@tags,Version=Version+1 WHERE Id=@id AND Tags<>@tags", db, tx))
                {
                    command.Parameters.AddWithValue("@tags", change.Item2);
                    command.Parameters.AddWithValue("@id", change.Item1);
                    command.ExecuteNonQuery();
                }

            using (var version = new SQLiteCommand("PRAGMA user_version=3", db, tx))
                version.ExecuteNonQuery();
        }

        private static void Consolidate(SQLiteConnection db, SQLiteTransaction tx)
        {
            AddColumn(db, tx, "Users", "Status", "TEXT NOT NULL DEFAULT 'Pending'");
            AddColumn(db, tx, "Users", "Tags", "TEXT NOT NULL DEFAULT ''");
            AddColumn(db, tx, "Users", "ShopCode", "TEXT");
            AddColumn(db, tx, "Users", "Version", "INTEGER NOT NULL DEFAULT 0");
            AddColumn(db, tx, "Users", "ApprovedBy", "INTEGER REFERENCES Users(Id)");
            AddColumn(db, tx, "Users", "UpdatedAt", "TEXT");
            string[] statements =
            {
                "UPDATE Users SET Status=(SELECT Status FROM AccountProfiles p WHERE p.UserId=Users.Id), Tags=(SELECT Tags FROM AccountProfiles p WHERE p.UserId=Users.Id), ShopCode=(SELECT ShopCode FROM AccountProfiles p WHERE p.UserId=Users.Id), Version=(SELECT Version FROM AccountProfiles p WHERE p.UserId=Users.Id), ApprovedBy=(SELECT ApprovedBy FROM AccountProfiles p WHERE p.UserId=Users.Id), UpdatedAt=(SELECT UpdatedAt FROM AccountProfiles p WHERE p.UserId=Users.Id) WHERE Id IN (SELECT UserId FROM AccountProfiles)",
                "CREATE TABLE Rentals_new (Id INTEGER PRIMARY KEY AUTOINCREMENT, OrderNumber TEXT, UserId INTEGER REFERENCES Users(Id), EquipmentId INTEGER REFERENCES Equipment(Id), QueueType TEXT, RentDate TEXT, ReturnDate TEXT, ActualReturnDate TEXT, RentalPrice REAL DEFAULT 0, Deposit REAL DEFAULT 0, PenaltyFee REAL DEFAULT 0, TotalPrice REAL DEFAULT 0, Status TEXT NOT NULL DEFAULT 'Pending', ConditionNotes TEXT, RequiresAdmin INTEGER DEFAULT 0, AdminNotes TEXT, CreatedAt TEXT, KioskId INTEGER REFERENCES Users(Id), ShopCode TEXT, Phone TEXT, RequestKey TEXT UNIQUE, Version INTEGER NOT NULL DEFAULT 0)",
                "INSERT INTO Rentals_new(Id,OrderNumber,UserId,EquipmentId,QueueType,RentDate,ReturnDate,ActualReturnDate,RentalPrice,Deposit,PenaltyFee,TotalPrice,Status,ConditionNotes,RequiresAdmin,AdminNotes,CreatedAt) SELECT Id,COALESCE(NULLIF(OrderNumber,''),'SD-' || printf('%06d',Id)),UserId,EquipmentId,QueueType,RentDate,ReturnDate,ActualReturnDate,RentalPrice,Deposit,PenaltyFee,TotalPrice,CASE WHEN Status='Overdue' THEN 'Active' WHEN Status='RequiresAdmin' THEN 'Pending' ELSE Status END,ConditionNotes,CASE WHEN Status='RequiresAdmin' THEN 1 ELSE RequiresAdmin END,COALESCE(AdminNotes,'') || CASE WHEN COALESCE(PickupCode,'')<>'' THEN char(10)||'Legacy pickup code: '||PickupCode ELSE '' END || CASE WHEN OperationType='Return' THEN char(10)||'Legacy operation: Return' ELSE '' END,CreatedAt FROM Rentals",
                "CREATE TEMP TABLE VisitOrderMap AS SELECT Id AS VisitId,Id+(SELECT COALESCE(MAX(Id),0) FROM Rentals) AS OrderId FROM KioskVisits",
                "INSERT INTO Rentals_new(Id,OrderNumber,UserId,QueueType,Status,KioskId,ShopCode,Phone,RequestKey,CreatedAt,AdminNotes) SELECT m.OrderId,'SD-'||printf('%06d',m.OrderId),v.ClientId,'Online',CASE WHEN v.Status='Waiting' THEN 'Pending' ELSE v.Status END,v.KioskId,v.ShopCode,v.Phone,v.RequestKey,v.CreatedAt,'Legacy ticket K-'||printf('%06d',v.Id)||'; state: '||v.Status FROM KioskVisits v JOIN VisitOrderMap m ON m.VisitId=v.Id",
                "CREATE TABLE ShopTransactions_new (Id INTEGER PRIMARY KEY AUTOINCREMENT, ClientId INTEGER NOT NULL REFERENCES Users(Id), OrderId INTEGER REFERENCES Rentals(Id), RelatedTransactionId INTEGER REFERENCES ShopTransactions(Id), Kind TEXT NOT NULL, AmountMinor INTEGER NOT NULL CHECK(AmountMinor>0), Notes TEXT, IsDemo INTEGER NOT NULL DEFAULT 1 CHECK(IsDemo=1), CreatedBy INTEGER NOT NULL REFERENCES Users(Id), CreatedAt TEXT NOT NULL, RequestKey TEXT NOT NULL UNIQUE)",
                "INSERT INTO ShopTransactions_new SELECT Id,ClientId,COALESCE(OrderId,(SELECT OrderId FROM VisitOrderMap m WHERE m.VisitId=t.VisitId)),RelatedTransactionId,Kind,AmountMinor,COALESCE(Notes,'') || CASE WHEN VisitId IS NOT NULL THEN char(10)||'Legacy ticket K-'||printf('%06d',VisitId) ELSE '' END,IsDemo,CreatedBy,CreatedAt,RequestKey FROM ShopTransactions t",
                "DROP TABLE ShopTransactions",
                "DROP TABLE Rentals",
                "ALTER TABLE Rentals_new RENAME TO Rentals",
                "ALTER TABLE ShopTransactions_new RENAME TO ShopTransactions",
                "DROP TABLE KioskVisits",
                "DROP TABLE AccountProfiles",
                "ALTER TABLE EquipmentRequests RENAME TO EquipmentChangeRequests",
                "DROP TABLE VisitOrderMap",
                "CREATE INDEX IX_Transactions_Client ON ShopTransactions(ClientId)",
                "CREATE INDEX IX_Transactions_Parent ON ShopTransactions(RelatedTransactionId)",
                "CREATE INDEX IX_Transactions_Order ON ShopTransactions(OrderId)",
                "CREATE INDEX IX_Users_Status ON Users(Status)",
                "CREATE INDEX IX_Rentals_Status ON Rentals(Status)"
            };
            foreach (var sql in statements)
                using (var command = new SQLiteCommand(sql, db, tx))
                    command.ExecuteNonQuery();
        }

        private static void AddColumn(SQLiteConnection db, SQLiteTransaction tx, string table, string column, string type)
        {
            bool found = false;
            using (var command = new SQLiteCommand("PRAGMA table_info(" + table + ")", db, tx))
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                    if (reader["name"].ToString() == column)
                        found = true;
            if (!found)
                using (var command = new SQLiteCommand("ALTER TABLE " + table + " ADD COLUMN " + column + " " + type, db, tx))
                    command.ExecuteNonQuery();
        }
    }
}
