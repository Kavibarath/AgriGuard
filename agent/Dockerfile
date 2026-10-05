# AgriGuard agent service: FastAPI + LangGraph on Python 3.12 (LangGraph lags the newest Python).
# Build from this folder:  docker build -t agriguard-agent agent
# Configuration is environment variables only (AGENT_*); nothing secret is baked into the image.
FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PIP_NO_CACHE_DIR=1 \
    PIP_DISABLE_PIP_VERSION_CHECK=1

WORKDIR /srv

COPY pyproject.toml ./
COPY app ./app
RUN pip install . \
    && useradd --system --uid 10001 --no-create-home agent

USER agent

# Render tells the service which port to bind through PORT; locally the default stays 8000.
EXPOSE 8000
CMD ["sh", "-c", "exec uvicorn app.main:app --host 0.0.0.0 --port ${PORT:-8000} --proxy-headers --forwarded-allow-ips='*'"]
