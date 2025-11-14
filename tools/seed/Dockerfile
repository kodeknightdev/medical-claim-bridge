FROM python:3.12.3-slim-bookworm
WORKDIR /seed
COPY requirements.txt .
RUN pip install --no-cache-dir --retries 10 --timeout 120 -r requirements.txt
COPY claimbridge_seed/ claimbridge_seed/
COPY seed.py .
RUN useradd --uid 10001 seed
USER seed
ENTRYPOINT ["python", "seed.py"]
