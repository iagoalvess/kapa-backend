# Decisões

As escolhas que não são óbvias olhando o código, com o motivo e **a condição que faria
reabri-las**. Decisão sem condição de revisão vira dogma: daqui a dois anos ninguém lembra se
aquilo foi pensado ou herdado.

Cada item é referenciado pelo número a partir do código (`ver docs/decisoes.md, item N`).

---

## 1. `Result<T>` para falha prevista, exceção para o resto

Todo método público de service devolve `Result` ou `Result<T>`. Falha que a regra previu
(e-mail duplicado, registro inexistente, sem permissão) volta como `Erro`; exceção fica para o
imprevisto e é traduzida pelo `GlobalExceptionHandler`.

**Por quê:** exceção como fluxo de controle esconde o caminho de erro do compilador — a
assinatura não diz que pode falhar, e quem chama descobre em produção. `Result` põe a falha no
tipo de retorno.

**Reabrir se:** o encadeamento de `Result` começar a exigir mais código do que resolve. O
sintoma é `if (x.Falhou) return ...` repetido cinco vezes no mesmo método.

## 2. Sem repositório genérico

Cada agregado tem repositório próprio, com métodos de intenção (`ObterParaEdicao`,
`RemoverInativosAnterioresA`).

**Por quê:** `Repository<T>` com `ObterTodos()` empurra a consulta para o chamador e espalha
regra de acesso a dado pela aplicação inteira. Método de intenção mantém a consulta perto de
quem sabe o que ela significa.

**Reabrir se:** aparecerem dez agregados com repositórios idênticos e sem nenhuma consulta
específica — aí o genérico deixa de ser abstração especulativa.

## 3. Refresh token opaco, não JWT

64 bytes de CSPRNG, guardados como hash SHA-256.

**Por quê:** refresh token precisa ser revogável, e revogar exige consulta ao banco. Se vai
bater no banco de qualquer forma, não há ganho em carregar dado assinado dentro dele. O hash
existe para que um vazamento do banco não entregue sessões utilizáveis — sem salt nem
alongamento porque os 512 bits de entropia não deixam nada a atacar por dicionário.

**Reabrir se:** surgir necessidade de validar o refresh token sem acesso ao banco.

## 4. Rotação obrigatória com detecção de reúso

Cada renovação revoga o token apresentado e emite outro. Token já rotacionado reaparecendo
derruba **todas** as sessões do usuário.

**Por quê:** é o único sinal observável de que a credencial foi copiada. Como não há como saber
se quem apresenta é o dono ou o atacante, derrubar tudo é a única resposta segura.

**Custo aceito:** duas abas renovando ao mesmo tempo podem derrubar a sessão. O front resolve
serializando a renovação (`sessao.renovar`, item 3 do `decisoes.md` do frontend).

## 5. Migration não roda no boot da aplicação

É um passo de deploy (`dotnet ef database update`), com imagem própria no Dockerfile.

**Por quê:** com duas réplicas subindo juntas, as duas migram e uma quebra. Pior: uma migration
destrutiva dentro de um container em loop de reinício aplica o estrago sozinha, repetidamente.

**Reabrir se:** nunca, para produção. Em ambiente de desenvolvimento de uma réplica só, o
`docker compose` já resolve com o serviço `migrations` separado.

## 6. Sem exceção customizada

Só exceções da BCL. O `ClassificadorDeExcecao` mapeia para status HTTP.

**Por quê:** hierarquia de exceção própria é código que só existe para ser traduzido de volta.
`FileNotFoundException` já diz o que precisa dizer.

## 7. E-mail em fila no banco, enviado pelo worker

Service chama `IEmailService.Enfileirar` e **não** salva — o e-mail entra na transação de quem o
originou.

**Por quê:** amarra o envio ao sucesso da operação. Se a transação volta atrás, o e-mail não
existe; se ela passa, o e-mail está garantido mesmo que o SMTP esteja fora do ar. Enviar dentro
da requisição acopla a resposta ao usuário à latência de um servidor de terceiro.

**Custo aceito:** se o processo morrer entre o envio e o registro, a mensagem fica presa em
`Enviando`. Escolha consciente entre "pode ficar preso" e "pode ser enviado duas vezes".

## 8. Fila de eventos em memória, com descarte

`Channel` limitado em 10.000, com `DropWrite`. O que é descartado é contado e sai no log.

**Por quê:** fila ilimitada é vazamento de memória com nome bonito — derruba o processo quando o
banco fica lento. E fazer a requisição do usuário esperar por espaço numa fila de *analytics*
inverte a prioridade: perder um ponto do gráfico é aceitável, atrasar a operação medida não é.

**Não use para:** cobrança ou auditoria legal. Evento que não pode ser perdido vai na transação
do domínio.

## 9. Download passa pela API, não por URL assinada

**Por quê:** URL assinada, uma vez emitida, vale para quem a tiver em mãos — mesmo que o usuário
tenha perdido o acesso no meio do caminho. Passando pela API, a autorização fica em um lugar só.

**Custo aceito:** arquivo grande ocupa a conexão da API pelo tempo da transferência.

**Reabrir se:** o tráfego de download virar gargalo. O ponto de mudança é acrescentar emissão de
URL temporária ao `IArmazenamentoDeArquivos`.

## 10. Tipo do conteúdo derivado da extensão, nunca do cliente

O `Content-Type` do multipart é ignorado; o service o deriva da extensão já validada contra a
lista de permissão. Extensão sem mapeamento vira `application/octet-stream`.

**Por quê:** o valor gravado é o que o download devolve. Aceitar o do cliente permitiria subir um
`.txt` anunciado como `text/html` e servi-lo de volta a partir do domínio da API — script do
atacante executando na origem da aplicação.

## 11. Cota por usuário, conferida de forma otimista

Espaço e quantidade, ambos ligados por padrão.

**Por quê:** limitar o tamanho de cada arquivo não limita nada — 120 requisições por minuto de
arquivos válidos enchem o bucket igual.

**Custo aceito:** dois envios simultâneos do mesmo usuário podem passar juntos e ultrapassar o
teto por um arquivo. Travar a linha do usuário serializaria todo envio do sistema em troca de um
excedente que o próximo envio já barra.

**Reabrir se:** o limite precisar ser rígido (cobrança por GB, cota contratual). Aí o lugar é uma
restrição no banco, não um lock na aplicação.

## 12. `X-Forwarded-For` só com proxy declarado

`Rede:ProxiesConfiaveis` vazio = cabeçalho ignorado.

**Por quê:** é um cabeçalho que qualquer cliente escreve. Aceitá-lo de qualquer origem
transformaria o rate limit em decoração — basta mandar um valor diferente a cada requisição.
Ligar por padrão seria pior que não tratar.

## 13. Rate limit em memória, por processo

`System.Threading.RateLimiting`, sem biblioteca.

**Por quê:** faz parte do runtime. É proteção contra abuso acidental e força bruta de senha, não
contra DDoS distribuído.

**Limitação conhecida:** com duas réplicas, o limite efetivo dobra.

**Reabrir se:** precisar de limite global preciso — aí o lugar é a borda (nginx, Cloudflare, API
gateway) ou um limitador com estado no Redis.

## 14. Jobs com `PeriodicTimer`, sem Hangfire nem Quartz

**Por quê:** o runtime já resolve. Hangfire traz painel, persistência de agendamento e retry — e
uma tabela, uma dependência e um modo de falha novos.

**Reabrir se:** precisar de **uma** das três: agendamento persistente, retry automático ou
painel. O job vira um método com `RecurringJob.AddOrUpdate` sem mudar nada em `Business`.

## 15. Sem EF Core InMemory nos testes

Integração roda contra Postgres real via Testcontainers.

**Por quê:** o provider InMemory não aplica `UNIQUE`, não aplica chave estrangeira e não executa
SQL. Teste passa e produção quebra — o pior resultado possível para uma suíte.

**Custo aceito:** os testes de integração exigem Docker.

## 17. Refresh token em cookie `HttpOnly`, e fora do corpo da resposta

`CookieDeSessao:Habilitado` ligado por padrão. O refresh token sai em `Set-Cookie`
(`HttpOnly; Secure; SameSite=Lax; Path=/api`) e **não** aparece no corpo de nenhuma resposta.

**Por quê:** tira a credencial de longa duração do alcance do JavaScript. No `localStorage`, um
XSS a copia e usa de outra máquina pelos dias de validade; em cookie `HttpOnly`, não há o que
copiar.

**As duas metades importam igualmente.** Manter o token fora do corpo é o que impede um XSS de
simplesmente chamar `/auth/refresh` e ler o token novo da resposta — o que anularia o `HttpOnly`
por completo. Pelo mesmo motivo, no modo cookie o corpo da requisição é **ignorado**, e não usado
como alternativa: aceitar os dois devolveria ao atacante o caminho que o cookie fechou.

**O que não resolve:** o XSS em si. O cookie viaja sozinho, então o atacante continua agindo como
o usuário enquanto a aba está aberta. "Sessão roubada por dias" vira "abuso enquanto a aba está
aberta" — melhora real, não cura.

**CSRF**, que o cookie traz de volta, tem duas camadas: `SameSite=Lax` fecha o caso comum, e a
checagem de `Origin` contra `Cors:Origens` cobre quem precisa de `SameSite=None` por ter front e
API em sites registráveis diferentes.

**`Secure` é incondicional**, inclusive em desenvolvimento: navegador trata `http://localhost`
como contexto seguro. Quem consome fora do navegador precisa falar `https` — é por isso que o
`ApiFactory` dos testes usa `https://localhost`.

**Desligar (`false`)** devolve o token ao corpo, para cliente que não é navegador — aplicativo
móvel, integração servidor a servidor. No navegador é retrocesso.

## 16. Nomenclatura no build, não só na IDE

`dotnet_diagnostic.IDE1006.severity = warning`, com regras separadas para `const` e
`static readonly` (PascalCase) e para o resto dos campos privados (`_camelCase`).

**Por quê:** regra que só o editor sublinha vira ruído que todo mundo aprende a ignorar. E uma
regra única para "campo privado" acusaria todo `static readonly Erro` do projeto, que está certo.
