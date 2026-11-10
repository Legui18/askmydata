# AGENTS.md

## Reglas
- Nunca leer, mostrar, editar ni crear el archivo .env. Nunca pedir ni escribir claves.
- Entorno: Windows + PowerShell. Comandos para PowerShell, no bash.
- Para probar la API usa Invoke-RestMethod, no curl.
- Responder en español, con pasos cortos.

## Proyecto
- Backend .NET 8 hexagonal en src/. No modificar el backend desde la tarea del frontend.
- Frontend en web/ (Angular standalone + TypeScript). El frontend llama siempre a la ruta relativa /api/ask; el dev server (puerto 4200) la redirige al backend en http://localhost:8080 con proxy.conf.json.
- Contrato: POST /api/ask con {question, conversationId?}.
  - 200: conversationId, sql, result{columns[{name,type}], rows[[...]]}, truncated,
    chartSuggestion{type,xColumn,yColumns[]}, explanation, metricUsage[], warnings[], usage{...}
  - 422: title, status, reasonCode "REFUSED", refusal, conversationId, correlationId, usage
- yColumns puede mezclar escalas (ingresos vs porcentaje): resolverlo en el gráfico.

## Arquitectura del frontend (web/)
- Angular standalone components, sin NgModules. TypeScript estricto.
- Carpetas en src/app/:
  - core/api/: AskApiService (único lugar que hace HTTP a /api/ask) y modelos tipados del contrato (AskResponse, RefusalResponse).
  - features/chat/: ChatPageComponent (contenedor, maneja conversationId y el historial en pantalla).
  - features/chat/components/: componentes de presentación pequeños: message-list, question-input, result-table, result-chart, sql-viewer (con botón copiar), adhoc-warning, refusal-message.
- Reglas:
  - Los componentes de presentación reciben datos por input() y no llaman a la API.
  - Solo AskApiService usa HttpClient. Manejar 200 y 422 ahí y devolver un tipo unión (éxito | rechazo).
  - Nada de `any`: tipar según el contrato.
  - El gráfico decide la escala por columna: si yColumns mezcla escalas, usar un segundo eje Y.
  - Sin librerías de UI pesadas; CSS simple.

## Diseño
- Estilo: limpio y sobrio, tipo herramienta de analítica. Tema claro con modo oscuro automático (prefers-color-scheme) usando variables CSS en :root.
- Una sola columna centrada (máx. 900px). Historial de mensajes arriba, caja de pregunta fija abajo.
- Cada respuesta es una tarjeta: explicación (texto), gráfico, tabla, y SQL colapsado con botón "Copiar". Aviso ad hoc como banda amarilla discreta. Rechazo (422) como tarjeta gris con el texto de refusal, sin tono de error.
- Tipografía del sistema (system-ui), espaciado generoso, bordes redondeados de 8px, un solo color de acento (azul).
- Estados: "pensando..." mientras carga, mensaje claro si la API no responde. Ejemplos de preguntas clicables en la pantalla vacía.
- Responsive: que se vea bien en móvil.