using FCatFlasher.Models;
using Microsoft.Data.Sqlite;
using System.IO;

namespace FCatFlasher.Services
{
    internal class LocalDatabaseService
    {
        private readonly string _connectionString;

        public LocalDatabaseService()
        {
            string databasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FCatFlasher",
                "offline.db");

            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

            _connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath
            }.ToString();
        }

        public void Initialize()
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS cached_users (
                    user_id TEXT PRIMARY KEY,
                    username TEXT NOT NULL UNIQUE,
                    full_name TEXT NOT NULL DEFAULT '',
                    password_hash TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS pending_device_registrations (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id TEXT NOT NULL,
                    order_id TEXT NOT NULL DEFAULT '',
                    serial_number TEXT NOT NULL,
                    mcu_id TEXT NOT NULL DEFAULT '',
                    mcu_name TEXT NOT NULL DEFAULT '',
                    firmware_name TEXT NOT NULL DEFAULT '',
                    registration_type TEXT NOT NULL DEFAULT 'PRODUCTION',
                    created_at_ticks INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS pending_box_reports (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id TEXT NOT NULL,
                    box_number INTEGER NOT NULL,
                    quantity INTEGER NOT NULL,
                    firmware_name TEXT NOT NULL DEFAULT '',
                    serial_numbers TEXT NOT NULL DEFAULT '',
                    first_serial TEXT NOT NULL DEFAULT '',
                    last_serial TEXT NOT NULL DEFAULT '',
                    created_at_ticks INTEGER NOT NULL
                );

                CREATE TABLE IF NOT EXISTS pending_rework_registrations (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id TEXT NOT NULL,
                    order_id TEXT NOT NULL DEFAULT '',
                    serial_number TEXT NOT NULL,
                    firmware_name TEXT NOT NULL DEFAULT '',
                    firmware_path TEXT NOT NULL DEFAULT '',
                    mcu_id TEXT NOT NULL DEFAULT '',
                    mcu_name TEXT NOT NULL DEFAULT '',
                    result TEXT NOT NULL DEFAULT 'OK',
                    created_at_ticks INTEGER NOT NULL
                );
                """;

            command.ExecuteNonQuery();
            EnsureCachedUsersTextId(connection);
            EnsureColumn(connection, "pending_device_registrations", "order_id", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(connection, "pending_device_registrations", "mcu_id", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(connection, "pending_device_registrations", "registration_type", "TEXT NOT NULL DEFAULT 'PRODUCTION'");
            EnsureColumn(connection, "pending_box_reports", "serial_numbers", "TEXT NOT NULL DEFAULT ''");
            EnsureColumn(connection, "pending_rework_registrations", "mcu_id", "TEXT NOT NULL DEFAULT ''");
            EnsureUniquePendingReworkSerials(connection);
        }

        public void CacheUser(CurrentUser user, string password)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO cached_users (user_id, username, full_name, password_hash)
                VALUES ($userId, $username, $fullName, $passwordHash)
                ON CONFLICT(user_id) DO UPDATE SET
                    username = excluded.username,
                    full_name = excluded.full_name,
                    password_hash = excluded.password_hash;
                """;

            command.Parameters.AddWithValue("$userId", user.Id);
            command.Parameters.AddWithValue("$username", user.Username);
            command.Parameters.AddWithValue("$fullName", user.FullName);
            command.Parameters.AddWithValue("$passwordHash", PasswordHasher.Hash(password));

            command.ExecuteNonQuery();
        }

        public CurrentUser? LoginOffline(string username, string password)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                SELECT user_id, username, full_name, password_hash
                FROM cached_users
                WHERE lower(username) = lower($username)
                LIMIT 1;
                """;

            command.Parameters.AddWithValue("$username", username.Trim());

            using SqliteDataReader reader = command.ExecuteReader();

            if (!reader.Read())
                return null;

            string passwordHash = reader.GetString(3);

            if (!PasswordHasher.Verify(password, passwordHash))
                return null;

            return new CurrentUser
            {
                Id = reader.GetString(0),
                Username = reader.GetString(1),
                FullName = reader.GetString(2),
                IsOffline = true
            };
        }

        public void AddPendingDeviceRegistration(
            string userId,
            string orderId,
            string serialNumber,
            string mcuId,
            string mcuName,
            string firmwareName,
            string registrationType)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO pending_device_registrations
                    (user_id, order_id, serial_number, mcu_id, mcu_name, firmware_name, registration_type, created_at_ticks)
                VALUES
                    ($userId, $orderId, $serialNumber, $mcuId, $mcuName, $firmwareName, $registrationType, $createdAtTicks)
                ON CONFLICT DO UPDATE SET
                    user_id = excluded.user_id,
                    order_id = excluded.order_id,
                    serial_number = excluded.serial_number,
                    mcu_id = excluded.mcu_id,
                    mcu_name = excluded.mcu_name,
                    firmware_name = excluded.firmware_name,
                    registration_type = excluded.registration_type,
                    created_at_ticks = excluded.created_at_ticks;
                """;

            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$orderId", orderId);
            command.Parameters.AddWithValue("$serialNumber", serialNumber);
            string normalizedMcuName = NormalizeMcuName(mcuName);
            string normalizedMcuId = NormalizeMcuName(mcuId);
            command.Parameters.AddWithValue("$mcuId", normalizedMcuId);
            command.Parameters.AddWithValue("$mcuName", normalizedMcuName);
            command.Parameters.AddWithValue("$firmwareName", firmwareName);
            command.Parameters.AddWithValue("$registrationType", registrationType);
            command.Parameters.AddWithValue("$createdAtTicks", DateTime.Now.Ticks);

            command.ExecuteNonQuery();
        }

        public void AddPendingBoxReport(string userId, BoxReport report)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                INSERT INTO pending_box_reports
                    (user_id, box_number, quantity, firmware_name, serial_numbers, first_serial, last_serial, created_at_ticks)
                VALUES
                    ($userId, $boxNumber, $quantity, $firmwareName, $serialNumbers, $firstSerial, $lastSerial, $createdAtTicks);
                """;

            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$boxNumber", report.BoxNumber);
            command.Parameters.AddWithValue("$quantity", report.Quantity);
            command.Parameters.AddWithValue("$firmwareName", report.FirmwareName);
            command.Parameters.AddWithValue("$serialNumbers", report.SerialNumbersText);
            command.Parameters.AddWithValue("$firstSerial", report.FirstSerial);
            command.Parameters.AddWithValue("$lastSerial", report.LastSerial);
            command.Parameters.AddWithValue("$createdAtTicks", DateTime.Now.Ticks);

            command.ExecuteNonQuery();
        }

        public void AddPendingReworkRegistration(
            string userId,
            string orderId,
            string serialNumber,
            string firmwareName,
            string firmwarePath,
            string mcuId,
            string mcuName,
            string result)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            string firmwareFileName = GetFileNameFromPath(firmwarePath);

            command.CommandText =
                """
                INSERT INTO pending_rework_registrations
                    (user_id, order_id, serial_number, firmware_name, firmware_path, mcu_id, mcu_name, result, created_at_ticks)
                VALUES
                    ($userId, $orderId, $serialNumber, $firmwareName, $firmwarePath, $mcuId, $mcuName, $result, $createdAtTicks)
                ON CONFLICT DO UPDATE SET
                    user_id = excluded.user_id,
                    order_id = excluded.order_id,
                    serial_number = excluded.serial_number,
                    firmware_name = excluded.firmware_name,
                    firmware_path = excluded.firmware_path,
                    mcu_id = excluded.mcu_id,
                    mcu_name = excluded.mcu_name,
                    result = excluded.result,
                    created_at_ticks = excluded.created_at_ticks;
                """;

            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$orderId", orderId);
            command.Parameters.AddWithValue("$serialNumber", serialNumber);
            command.Parameters.AddWithValue("$firmwareName", firmwareName);
            command.Parameters.AddWithValue("$firmwarePath", firmwareFileName);
            string normalizedMcuName = NormalizeMcuName(mcuName);
            string normalizedMcuId = NormalizeMcuName(mcuId);
            command.Parameters.AddWithValue("$mcuId", normalizedMcuId);
            command.Parameters.AddWithValue("$mcuName", normalizedMcuName);
            command.Parameters.AddWithValue("$result", result);
            command.Parameters.AddWithValue("$createdAtTicks", DateTime.Now.Ticks);

            command.ExecuteNonQuery();
        }

        public IReadOnlyList<BoxReport> LoadPendingBoxReports()
        {
            return LoadPendingBoxReportsFrom(null);
        }

        public IReadOnlyList<BoxReport> LoadPendingBoxReportsFrom(DateTime from)
        {
            return LoadPendingBoxReportsFrom(from.Date.Ticks);
        }

        private IReadOnlyList<BoxReport> LoadPendingBoxReportsFrom(long? fromTicks)
        {
            var reports = new List<BoxReport>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                SELECT box_number, quantity, firmware_name, serial_numbers, first_serial, last_serial, created_at_ticks
                FROM pending_box_reports
                WHERE $fromTicks IS NULL OR created_at_ticks >= $fromTicks
                ORDER BY id DESC;
                """;

            command.Parameters.AddWithValue("$fromTicks", fromTicks.HasValue ? fromTicks.Value : DBNull.Value);

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                reports.Add(new BoxReport
                {
                    BoxNumber = reader.GetInt32(0),
                    Quantity = reader.GetInt32(1),
                    FirmwareName = reader.GetString(2),
                    SerialNumbersText = reader.GetString(3),
                    FirstSerial = reader.GetString(4),
                    LastSerial = reader.GetString(5),
                    CreatedAt = new DateTime(reader.GetInt64(6))
                });
            }

            return reports;
        }

        public IReadOnlyList<ReworkSummary> LoadPendingReworkSummaries()
        {
            return LoadPendingReworkSummariesFrom(null);
        }

        public IReadOnlyList<FirmwareReportRow> LoadProductionFirmwareReport(DateTime from, DateTime to)
        {
            var rows = new List<FirmwareReportRow>();
            (long fromTicks, long toTicks) = GetInclusiveDateRangeTicks(from, to);

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                SELECT
                    firmware_name,
                    COUNT(*),
                    MIN(created_at_ticks),
                    MAX(created_at_ticks)
                FROM pending_device_registrations
                WHERE registration_type = 'PRODUCTION'
                  AND created_at_ticks >= $fromTicks
                  AND created_at_ticks < $toTicks
                GROUP BY firmware_name
                ORDER BY MAX(created_at_ticks) DESC;
                """;

            command.Parameters.AddWithValue("$fromTicks", fromTicks);
            command.Parameters.AddWithValue("$toTicks", toTicks);

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                rows.Add(new FirmwareReportRow
                {
                    FirmwareName = reader.GetString(0),
                    Quantity = reader.GetInt32(1),
                    FirstCreatedAt = new DateTime(reader.GetInt64(2)),
                    LastCreatedAt = new DateTime(reader.GetInt64(3))
                });
            }

            return rows;
        }

        public IReadOnlyList<FirmwareReportRow> LoadReworkFirmwareReport(DateTime from, DateTime to)
        {
            var rows = new List<FirmwareReportRow>();
            (long fromTicks, long toTicks) = GetInclusiveDateRangeTicks(from, to);

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                SELECT
                    order_id,
                    firmware_name,
                    COUNT(*),
                    MIN(created_at_ticks),
                    MAX(created_at_ticks)
                FROM pending_rework_registrations
                WHERE created_at_ticks >= $fromTicks
                  AND created_at_ticks < $toTicks
                GROUP BY order_id, firmware_name
                ORDER BY MAX(created_at_ticks) DESC;
                """;

            command.Parameters.AddWithValue("$fromTicks", fromTicks);
            command.Parameters.AddWithValue("$toTicks", toTicks);

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                rows.Add(new FirmwareReportRow
                {
                    OrderId = reader.GetString(0),
                    FirmwareName = reader.GetString(1),
                    Quantity = reader.GetInt32(2),
                    FirstCreatedAt = new DateTime(reader.GetInt64(3)),
                    LastCreatedAt = new DateTime(reader.GetInt64(4))
                });
            }

            return rows;
        }

        public IReadOnlyList<ReworkSummary> LoadPendingReworkSummariesFrom(DateTime from)
        {
            return LoadPendingReworkSummariesFrom(from.Date.Ticks);
        }

        private IReadOnlyList<ReworkSummary> LoadPendingReworkSummariesFrom(long? fromTicks)
        {
            var summaries = new List<ReworkSummary>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                SELECT
                    order_id,
                    firmware_name,
                    COUNT(*),
                    MIN(created_at_ticks),
                    MAX(created_at_ticks)
                FROM pending_rework_registrations
                WHERE $fromTicks IS NULL OR created_at_ticks >= $fromTicks
                GROUP BY order_id, firmware_name
                ORDER BY MAX(created_at_ticks) DESC;
                """;

            command.Parameters.AddWithValue("$fromTicks", fromTicks.HasValue ? fromTicks.Value : DBNull.Value);

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                summaries.Add(new ReworkSummary
                {
                    OrderId = reader.GetString(0),
                    FirmwareName = reader.GetString(1),
                    Quantity = reader.GetInt32(2),
                    FirstCreatedAt = new DateTime(reader.GetInt64(3)),
                    LastCreatedAt = new DateTime(reader.GetInt64(4))
                });
            }

            return summaries;
        }

        public ReworkOrderInfo? LoadPendingReworkOrder(string orderId)
        {
            if (string.IsNullOrWhiteSpace(orderId))
                return null;

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                SELECT order_id, firmware_name, firmware_path
                FROM pending_rework_registrations
                WHERE lower(order_id) = lower($orderId)
                ORDER BY created_at_ticks DESC
                LIMIT 1;
                """;

            command.Parameters.AddWithValue("$orderId", orderId.Trim());

            using SqliteDataReader reader = command.ExecuteReader();

            if (!reader.Read())
                return null;

            return new ReworkOrderInfo
            {
                OrderId = reader.GetString(0),
                FirmwareName = reader.GetString(1),
                FirmwarePath = reader.GetString(2)
            };
        }

        public void DeletePendingReworks(string orderId, string firmwareName)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                DELETE FROM pending_rework_registrations
                WHERE order_id = $orderId
                  AND firmware_name = $firmwareName;
                """;

            command.Parameters.AddWithValue("$orderId", orderId);
            command.Parameters.AddWithValue("$firmwareName", firmwareName);

            command.ExecuteNonQuery();
        }

        public IReadOnlyList<PendingRegistration> LoadPendingRegistrations()
        {
            var registrations = new List<PendingRegistration>();

            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                SELECT
                    d.order_id,
                    d.serial_number,
                    d.firmware_name,
                    d.created_at_ticks,
                    d.mcu_name
                FROM pending_device_registrations d
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM pending_rework_registrations r
                    WHERE r.serial_number = d.serial_number
                )

                UNION ALL

                SELECT
                    r.order_id,
                    r.serial_number,
                    r.firmware_name,
                    r.created_at_ticks,
                    r.mcu_name
                FROM pending_rework_registrations r

                ORDER BY 4 DESC;
                """;

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                registrations.Add(new PendingRegistration
                {
                    OrderId = reader.GetString(0),
                    SerialNumber = reader.GetString(1),
                    FirmwareName = reader.GetString(2),
                    CreatedAt = new DateTime(reader.GetInt64(3)),
                    McuName = reader.GetString(4)
                });
            }

            return registrations;
        }

        public int CountPendingDeviceRegistrationsFrom(DateTime from, string registrationType)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                SELECT COUNT(*)
                FROM pending_device_registrations
                WHERE created_at_ticks >= $fromTicks
                  AND registration_type = $registrationType;
                """;

            command.Parameters.AddWithValue("$fromTicks", from.Date.Ticks);
            command.Parameters.AddWithValue("$registrationType", registrationType);

            return Convert.ToInt32(command.ExecuteScalar());
        }

        public int CountPendingReworksFrom(DateTime from)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText =
                """
                SELECT COUNT(*)
                FROM pending_rework_registrations
                WHERE created_at_ticks >= $fromTicks;
                """;

            command.Parameters.AddWithValue("$fromTicks", from.Date.Ticks);

            return Convert.ToInt32(command.ExecuteScalar());
        }

        public void SyncPending(ApiService apiService)
        {
            SyncPendingDevices(apiService);
            SyncPendingBoxes(apiService);
            SyncPendingReworks(apiService);
        }

        private void SyncPendingDevices(ApiService apiService)
        {
            using var connection = OpenConnection();
            using var selectCommand = connection.CreateCommand();

            selectCommand.CommandText =
                """
                SELECT id, user_id, serial_number, mcu_name, firmware_name, registration_type
                FROM pending_device_registrations
                ORDER BY id;
                """;

            using SqliteDataReader reader = selectCommand.ExecuteReader();

            while (reader.Read())
            {
                int id = reader.GetInt32(0);

                apiService.AddDeviceRegistration(
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5));

                DeleteById("pending_device_registrations", id);
            }
        }

        private void SyncPendingBoxes(ApiService apiService)
        {
            using var connection = OpenConnection();
            using var selectCommand = connection.CreateCommand();

            selectCommand.CommandText =
                """
                SELECT id, user_id, box_number, quantity, firmware_name, serial_numbers, first_serial, last_serial
                FROM pending_box_reports
                ORDER BY id;
                """;

            using SqliteDataReader reader = selectCommand.ExecuteReader();

            while (reader.Read())
            {
                int id = reader.GetInt32(0);

                apiService.AddBoxReport(
                    reader.GetString(1),
                    new BoxReport
                    {
                        BoxNumber = reader.GetInt32(2),
                        Quantity = reader.GetInt32(3),
                        FirmwareName = reader.GetString(4),
                        SerialNumbersText = reader.GetString(5),
                        FirstSerial = reader.GetString(6),
                        LastSerial = reader.GetString(7)
                    });

                DeleteById("pending_box_reports", id);
            }
        }

        private void SyncPendingReworks(ApiService apiService)
        {
            using var connection = OpenConnection();
            using var selectCommand = connection.CreateCommand();

            selectCommand.CommandText =
                """
                SELECT id, user_id, order_id, serial_number, firmware_name, firmware_path, mcu_id, mcu_name, result
                FROM pending_rework_registrations
                ORDER BY id;
                """;

            using SqliteDataReader reader = selectCommand.ExecuteReader();

            while (reader.Read())
            {
                int id = reader.GetInt32(0);

                apiService.AddReworkRegistration(
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8));

                DeleteById("pending_rework_registrations", id);
            }
        }

        private void DeleteById(string tableName, int id)
        {
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();

            command.CommandText = $"DELETE FROM {tableName} WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            command.ExecuteNonQuery();
        }

        private static void EnsureColumn(
            SqliteConnection connection,
            string tableName,
            string columnName,
            string columnDefinition)
        {
            using var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = $"PRAGMA table_info({tableName});";

            using SqliteDataReader reader = checkCommand.ExecuteReader();

            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            reader.Close();

            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};";
            alterCommand.ExecuteNonQuery();
        }

        private static void EnsureCachedUsersTextId(SqliteConnection connection)
        {
            using var checkCommand = connection.CreateCommand();
            checkCommand.CommandText = "PRAGMA table_info(cached_users);";

            using SqliteDataReader reader = checkCommand.ExecuteReader();

            while (reader.Read())
            {
                if (!string.Equals(reader.GetString(1), "user_id", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool isTextId = string.Equals(reader.GetString(2), "TEXT", StringComparison.OrdinalIgnoreCase);

                if (isTextId)
                    return;

                break;
            }

            reader.Close();

            using var migrateCommand = connection.CreateCommand();
            migrateCommand.CommandText =
                """
                ALTER TABLE cached_users RENAME TO cached_users_old;

                CREATE TABLE cached_users (
                    user_id TEXT PRIMARY KEY,
                    username TEXT NOT NULL UNIQUE,
                    full_name TEXT NOT NULL DEFAULT '',
                    password_hash TEXT NOT NULL
                );

                INSERT OR IGNORE INTO cached_users (user_id, username, full_name, password_hash)
                SELECT CAST(user_id AS TEXT), username, full_name, password_hash
                FROM cached_users_old;

                DROP TABLE cached_users_old;
                """;
            migrateCommand.ExecuteNonQuery();
        }

        private static void EnsureUniquePendingReworkSerials(SqliteConnection connection)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE pending_rework_registrations
                SET mcu_id = mcu_name
                WHERE mcu_id = ''
                  AND mcu_name <> '';

                UPDATE pending_device_registrations
                SET mcu_id = mcu_name
                WHERE mcu_id = ''
                  AND mcu_name <> '';

                UPDATE pending_device_registrations
                SET mcu_name = 'STM32G0',
                    mcu_id = ''
                WHERE mcu_name = 'G0'
                   OR mcu_id IN ('G0', 'STM32G0');

                UPDATE pending_device_registrations
                SET mcu_name = 'STM32C0',
                    mcu_id = ''
                WHERE mcu_name = 'C0'
                   OR mcu_id IN ('C0', 'STM32C0');

                UPDATE pending_rework_registrations
                SET mcu_name = 'STM32G0',
                    mcu_id = ''
                WHERE mcu_name = 'G0'
                   OR mcu_id IN ('G0', 'STM32G0');

                UPDATE pending_rework_registrations
                SET mcu_name = 'STM32C0',
                    mcu_id = ''
                WHERE mcu_name = 'C0'
                   OR mcu_id IN ('C0', 'STM32C0');

                DELETE FROM pending_rework_registrations
                WHERE id NOT IN (
                    SELECT existing.id
                    FROM pending_rework_registrations existing
                    WHERE NOT EXISTS (
                        SELECT 1
                        FROM pending_rework_registrations newer
                        WHERE lower(newer.serial_number) = lower(existing.serial_number)
                          AND (
                              newer.created_at_ticks > existing.created_at_ticks
                              OR (
                                  newer.created_at_ticks = existing.created_at_ticks
                                  AND newer.id > existing.id
                              )
                          )
                    )
                );

                DELETE FROM pending_device_registrations
                WHERE registration_type = 'REWORK'
                  AND id NOT IN (
                      SELECT existing.id
                      FROM pending_device_registrations existing
                      WHERE existing.registration_type = 'REWORK'
                        AND NOT EXISTS (
                            SELECT 1
                            FROM pending_device_registrations newer
                            WHERE newer.registration_type = 'REWORK'
                              AND lower(newer.serial_number) = lower(existing.serial_number)
                              AND (
                                  newer.created_at_ticks > existing.created_at_ticks
                                  OR (
                                      newer.created_at_ticks = existing.created_at_ticks
                                      AND newer.id > existing.id
                                  )
                              )
                        )
                  );

                CREATE UNIQUE INDEX IF NOT EXISTS ux_pending_rework_registrations_serial_number
                ON pending_rework_registrations (serial_number COLLATE NOCASE);

                CREATE UNIQUE INDEX IF NOT EXISTS ux_pending_device_rework_serial_number
                ON pending_device_registrations (serial_number COLLATE NOCASE)
                WHERE registration_type = 'REWORK';
                """;

            command.ExecuteNonQuery();
        }

        private static string GetFileNameFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            string trimmedPath = path.Trim();
            int slashIndex = trimmedPath.LastIndexOf('/');
            int backslashIndex = trimmedPath.LastIndexOf('\\');
            int separatorIndex = Math.Max(slashIndex, backslashIndex);

            return separatorIndex >= 0 && separatorIndex < trimmedPath.Length - 1
                ? trimmedPath[(separatorIndex + 1)..]
                : trimmedPath;
        }

        private static (long FromTicks, long ToTicks) GetInclusiveDateRangeTicks(DateTime from, DateTime to)
        {
            DateTime fromDate = from.Date;
            DateTime toExclusive = to.Date.AddDays(1);

            if (toExclusive <= fromDate)
                toExclusive = fromDate.AddDays(1);

            return (fromDate.Ticks, toExclusive.Ticks);
        }

        private static string NormalizeMcuName(string mcuName)
        {
            return mcuName switch
            {
                "G0" => "STM32G0",
                "C0" => "STM32C0",
                _ => mcuName
            };
        }

        private SqliteConnection OpenConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();

            return connection;
        }
    }
}
