# syntax=docker/dockerfile:1.7
FROM python:3.12.3-slim-bookworm

# Microsoft ODBC Driver 18 for SQL Server (pyodbc)
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl gnupg ca-certificates \
 && curl -fsSL https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor -o /usr/share/keyrings/microsoft-prod.gpg \
 && echo "deb [arch=amd64,arm64 signed-by=/usr/share/keyrings/microsoft-prod.gpg] https://packages.microsoft.com/debian/12/prod bookworm main" > /etc/apt/sources.list.d/mssql-release.list \
 && apt-get update \
 && ACCEPT_EULA=Y apt-get install -y --no-install-recommends msodbcsql18 unixodbc \
 && apt-get purge -y curl gnupg && apt-get autoremove -y && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY requirements.txt .
RUN --mount=type=cache,target=/root/.cache/pip pip install --retries 10 --timeout 120 -r requirements.txt
COPY . .

RUN useradd --uid 10001 --create-home app && mkdir -p /app/models && chown -R app /app
USER app
ENV PYTHONUNBUFFERED=1
EXPOSE 8000
ENTRYPOINT ["./docker-entrypoint.sh"]
