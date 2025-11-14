#!/bin/sh
# SQL Server can take a while to accept logins in compose; retry the migration before starting the API.
set -e
for i in $(seq 1 30); do
  if alembic upgrade head; then
    exec uvicorn app.main:app --host 0.0.0.0 --port 8000
  fi
  echo "database not ready (attempt $i), retrying"
  sleep 5
done
echo "giving up on database migrations"
exit 1
