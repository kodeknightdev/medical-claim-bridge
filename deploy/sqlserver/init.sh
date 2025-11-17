#!/bin/bash
# One database and one login per service; a login is db_owner of its own database only.
set -euo pipefail
SQLCMD=/opt/mssql-tools18/bin/sqlcmd
HOST="${SQL_HOST:-sqlserver}"

until $SQLCMD -S "$HOST" -U sa -P "$MSSQL_SA_PASSWORD" -C -Q "SELECT 1" >/dev/null 2>&1; do
  echo "waiting for sql server"
  sleep 3
done

for svc in identity providers claims scrubber payers remittance notifications denial_predictor analytics; do
  db="cb_${svc}"
  login="${svc}_svc"
  $SQLCMD -S "$HOST" -U sa -P "$MSSQL_SA_PASSWORD" -C -b -Q "
    IF DB_ID('$db') IS NULL CREATE DATABASE [$db];
    IF SUSER_ID('$login') IS NULL CREATE LOGIN [$login] WITH PASSWORD = '$SERVICE_DB_PASSWORD', CHECK_POLICY = OFF;
    ALTER LOGIN [$login] WITH DEFAULT_DATABASE = [$db];
  "
  $SQLCMD -S "$HOST" -U sa -P "$MSSQL_SA_PASSWORD" -C -b -d "$db" -Q "
    IF USER_ID('$login') IS NULL CREATE USER [$login] FOR LOGIN [$login];
    ALTER ROLE db_owner ADD MEMBER [$login];
  "
  echo "database $db ready for $login"
done
