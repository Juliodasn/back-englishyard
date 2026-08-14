# Regras operacionais

## Status de alunos

- **Ativo**: participa normalmente da agenda.
- **Experimental**: participa da agenda enquanto estiver em avaliação.
- **Inadimplente**: continua na agenda e no histórico; a situação financeira não cancela aulas automaticamente.
- **Pendente**: permanece cadastrado, mas não pode receber novas aulas nem aparecer em projeções futuras.
- **Arquivado (`ativo=false`)**: não aparece na operação futura. O histórico é preservado e o cadastro pode ser restaurado; a agenda deve ser configurada novamente.

## Status de professoras

- **Ativa**: acessa o portal, recebe aulas e registra ocorrências.
- **Em onboarding**: acessa o portal, mas ainda não recebe aulas.
- **Em férias**: acessa o portal e consulta/registra histórico, mas não recebe novas aulas nem aparece na projeção atual.
- **Pausada**: não acessa a API, não recebe aulas e não registra ocorrências.
- **Arquivada (`ativo=false`)**: histórico preservado; ao restaurar, volta como Pausada e exige reativação explícita e, se necessário, recriação do acesso.

## Grupo/turma

Nesta versão, “grupo” significa alunos que compartilham professora, data e faixa de horário. Não existe uma turma fixa com identidade própria. Se a escola passar a operar turmas nomeadas com matrícula e vigência próprias, isso deverá ser introduzido como uma nova entidade, sem inferi-la dos horários existentes.
