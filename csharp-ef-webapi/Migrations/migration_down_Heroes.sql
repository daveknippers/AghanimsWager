START TRANSACTION;

DROP TABLE "Kali".dota_heroes;

DELETE FROM "__EFMigrationsHistory"
WHERE "MigrationId" = '20231231102444_Heroes';

COMMIT;


