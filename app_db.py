import datetime

import sqlalchemy as db
from sqlalchemy import exc, text

import logging
from enum import Enum, auto, IntEnum


class MATCH_STATUS(IntEnum):
	UNRESOLVED = auto()
	RADIANT = auto()
	DIRE = auto()
	ERROR = auto()

class LP_STATUS(Enum):
	NOT_FOUND = auto()	
	INIT = auto()	
	VALID = auto()	
	INVALID = auto()	

class PGDB:

	def __init__(self,conn_string):
		self.engine = db.create_engine(conn_string,isolation_level="AUTOCOMMIT")
		self.conn = self.engine.connect()
		self.metadata = db.MetaData(schema='Kali')

		self.bet_ledger = db.Table('bet_ledger', self.metadata,
			db.Column('match_id',db.BigInteger,nullable=False),
			db.Column('gambler_id',db.BigInteger,nullable=False),
			db.Column('bet_side',db.String(7),nullable=False),
			db.Column('amount',db.BigInteger,nullable=False),
			db.Column('finalized',db.Integer,nullable=False))

		self.charity = db.Table('charity', self.metadata,
			db.Column('gambler_id',db.BigInteger,nullable=False),
			db.Column('amount',db.BigInteger,nullable=False))

		self.balance_ledger = db.Table('balance_ledger',self.metadata,
			db.Column('discord_id',db.BigInteger,primary_key=True,nullable=False),
			db.Column('tokens',db.BigInteger,nullable=False))

		self.live_lobbies = db.Table('live_lobbies', self.metadata,
			db.Column('lobby_id',db.BigInteger,nullable=False))

		self.match_status = db.Table('match_status', self.metadata,
			db.Column('match_id',db.BigInteger,nullable=False),
			db.Column('status',db.Integer,nullable=False))

		self.lobby_message = db.Table('lobby_message', self.metadata,
			db.Column('lobby_id',db.BigInteger,nullable=False,primary_key=True),
			db.Column('message_id',db.BigInteger,nullable=False))

		self.live_matches = db.Table('live_matches', self.metadata,
			db.Column('query_time',db.BigInteger,nullable=False),
			db.Column('activate_time',db.BigInteger,nullable=False),
			db.Column('deactivate_time',db.BigInteger,nullable=False),
			db.Column('server_steam_id',db.BigInteger,nullable=False),
			db.Column('lobby_id',db.BigInteger,nullable=False),
			db.Column('league_id',db.Integer,nullable=False),
			db.Column('lobby_type',db.Integer,nullable=False),
			db.Column('game_time',db.Integer,nullable=False),
			db.Column('delay',db.Integer,nullable=False),
			db.Column('spectators',db.Integer,nullable=False),
			db.Column('game_mode',db.Integer,nullable=False),
			db.Column('average_mmr',db.Integer,nullable=False),
			db.Column('match_id',db.BigInteger,nullable=False),
			db.Column('series_id',db.Integer,nullable=False),
			db.Column('sort_score',db.Integer,nullable=False),
			db.Column('last_update_time',db.Float,nullable=False),
			db.Column('radiant_lead',db.Integer,nullable=False),
			db.Column('radiant_score',db.Integer,nullable=False),
			db.Column('dire_score',db.Integer,nullable=False),
			db.Column('building_state',db.Integer,nullable=False))

		self.friends = db.Table('friends', self.metadata,
			db.Column('steam_id',db.BigInteger,nullable=False))

		self.discord_ids = db.Table('discord_ids', self.metadata,
			db.Column('discord_id',db.BigInteger,nullable=False),
			db.Column('steam_id',db.BigInteger,nullable=False))

		self.live_players = db.Table('live_players', self.metadata,
			db.Column('match_id',db.BigInteger,nullable=False),
			db.Column('player_num',db.Integer,nullable=False),
			db.Column('account_id',db.BigInteger,nullable=False),
			db.Column('hero_id',db.Integer,nullable=False))

		self.rp_heroes = db.Table('rp_heroes', self.metadata,
			db.Column('lobby_id',db.BigInteger,nullable=False,primary_key=True),
			db.Column('steam_id',db.BigInteger,nullable=False,primary_key=True),
			db.Column('param0',db.String,nullable=True,primary_key=True),
			db.Column('param1',db.String,nullable=True,primary_key=True),
			db.Column('param2',db.String,nullable=True))

		self.metadata.create_all(self.engine)

	def select_discord_ids(self):
		di = self.discord_ids
		q = db.select([di])
		result = self.conn.execute(q).fetchall()
		return result
		
	def insert_discord_id(self,discord_id,steam_id):
		di = self.discord_ids
		q = db.select([di.c.discord_id]).where(di.c.discord_id == discord_id)
		result = self.conn.execute(q).fetchall()
		if len(result) == 0:
			insert = di.insert().values(discord_id=discord_id,steam_id=steam_id)
			return self.conn.execute(insert)
		elif len(result) == 1:
			update = di.update().values(steam_id = steam_id).where(di.c.discord_id == discord_id)
			return self.conn.execute(update)
		elif len(result) > 1:
			print('Insert discord_id/steam_id failed for {} / {}, multiple results returned'.format(discord_id,steam_id))
			return None

	def update_match_status(self,match_id,status):
		ms = self.match_status
		update = ms.update().values(status = int(status)).where(ms.c.match_id == match_id)
		result = self.conn.execute(update)
		if result.rowcount != 1:
			logging.warning('update match status returned {} results'.format(results.rowcount))

	def insert_match_status(self,match_id):
		ms = self.match_status
		insert = ms.insert().values(match_id=match_id,status=int(MATCH_STATUS.UNRESOLVED))
		result = self.conn.execute(insert)

	def check_match_status(self,match_id):
		ms = self.match_status
		q = db.select([ms.c.status]).where(ms.c.match_id == match_id)
		result = self.conn.execute(q).fetchall()
		return result

	def get_unresolved_matches(self):
		q = '''SELECT DISTINCT lm.lobby_id
FROM "Kali".match_status AS ms
LEFT OUTER JOIN "Kali".live_matches as lm
ON lm.match_id = ms.match_id
WHERE ms.status = 1'''
		result = self.conn.execute(q).fetchall()
		return result

	def replace_friends(self,friend_ids):
		begin_statement = '''BEGIN WORK;
LOCK TABLE "Kali".friends IN ACCESS EXCLUSIVE MODE;'''
		delete = str(self.friends.delete())+';'
		end_statement = text('COMMIT WORK;')
		friend_template = text('INSERT INTO "Kali".friends VALUES (:steam_id)')
		friend_statements = []
		for f in friend_ids:
			friend_statements.append(str(friend_template.bindparams(steam_id = f).compile(compile_kwargs={"literal_binds": True}))+';')
		friend_statement = '\n'.join(friend_statements)
		lock_statement = '''{}
{}
{}
{}'''.format(begin_statement,delete,friend_statement,end_statement)
		print(lock_statement)
		result = self.conn.execute(lock_statement)
		print('result rowcount:',result.rowcount)

	def select_friends(self):
		f = self.friends
		q = db.select([f])
		result = self.conn.execute(q).fetchall()
		return result
		
	def replace_live(self,lobbies):
		begin_statement = '''BEGIN WORK;
LOCK TABLE "Kali".live_lobbies IN ACCESS EXCLUSIVE MODE;'''
		delete = str(self.live_lobbies.delete())+';'
		end_statement = text('COMMIT WORK;')
		lobby_template = text('INSERT INTO "Kali".live_lobbies VALUES (:lobby_id)')
		lobby_statements = []
		for l in lobbies:
			lobby_statements.append(str(lobby_template.bindparams(lobby_id = l).compile(compile_kwargs={"literal_binds": True}))+';')
		lobby_statement = '\n'.join(lobby_statements)
		lock_statement = '''{}
{}
{}
{}'''.format(begin_statement,delete,lobby_statement,end_statement)
		print(lock_statement)
		result = self.conn.execute(lock_statement)
		print('result rowcount:',result.rowcount)

	def get_live(self):
		ll = self.live_lobbies
		q = db.select([ll])
		result = self.conn.execute(q).fetchall()
		return result

	def insert_match_message(self,lobby_id,message_id):
		lm = self.lobby_message
		insert = lm.insert().values(lobby_id=lobby_id,message_id=message_id)
		result = self.conn.execute(insert)

	def select_match_message(self,lobby_id):
		lm = self.lobby_message
		s = db.select([lm.c.message_id]).where(lm.c.lobby_id == lobby_id)
		return self.conn.execute(s).fetchall()

	def select_lm(self,lobby_id,last_update_time=None,query_only=False):
		lm = self.live_matches
		if last_update_time == None:
			s = db.select([lm]).where(lm.c.lobby_id == lobby_id)
		else:
			s = db.select([lm]).where(db.and_(lm.c.lobby_id == lobby_id,
											  lm.c.last_update_time >= last_update_time))
		if query_only:
			return s
		else:
			results = self.conn.execute(s)
			keys = results.keys()
			results = results.fetchall()
			if len(results) == 0:
				print('select_lm returned 0 results (expected race when live_lobbies updates before live_matches)')
				print('lobby_id: {}'.format(lobby_id))
				print('last_update_time: {}'.format(last_update_time))
				print('query: {}'.format(str(s)))
			return results,keys

	def insert_lm(self,row):
		lm = self.live_matches
		insert = lm.insert().values(**row)
		result = self.conn.execute(insert)
	
	def insert_lp(self,players):
		lp = self.live_players
		insert = lp.insert().values(players)
		result = self.conn.execute(insert)
		if any(map(lambda x: x == 0,map(lambda x: x['hero_id'],players))):
			return LP_STATUS.INIT
		else:
			return LP_STATUS.VALID

	def update_lp(self,players):
		lp = self.live_players
		begin_statement = '''BEGIN WORK;
LOCK TABLE "Kali".live_players IN ACCESS EXCLUSIVE MODE;'''
		end_statement = text('COMMIT WORK;')
		lp_template = text('UPDATE "Kali".live_players SET hero_id = (:hero_id) WHERE match_id = (:match_id) AND player_num = (:player_num) AND account_id = (:account_id);')
		update_statements = []
		for p in players:
			update_statements.append(str(lp_template.bindparams(hero_id = p['hero_id'], match_id = p['match_id'], player_num = p['player_num'], account_id=p['account_id']).compile(compile_kwargs={"literal_binds": True}))+';')

		updates = '\n'.join(update_statements)
		lock_statement = '''{}
{}
{}'''.format(begin_statement,updates,end_statement)
		result = self.conn.execute(lock_statement)

	def check_lp(self,match_id):
		lp = self.live_players
		s = db.select([lp.c.hero_id]).where(lp.c.match_id == match_id)
		logging.info('executing: {}'.format(s))
		if (result := self.conn.execute(s).fetchall()):
			if len(result) != 10:
				logging.warning('Unexpected number of players in match_id {}'.format(match_id))
				return LP_STATUS.INVALID
			elif any(map(lambda x: x == 0,map(lambda x: x['hero_id'],result))):
				return LP_STATUS.INIT
			else:
				return LP_STATUS.VALID
		else:
			logging.info('no live players found for match_id = {}'.format(match_id))
			return LP_STATUS.NOT_FOUND

	def read_lp(self,match_id):
		lp = self.live_players
		s = db.select([lp]).where(lp.c.match_id == match_id)
		results = self.conn.execute(s)
		keys = results.keys()
		results = results.fetchall()
		return results,keys

	def insert_rp(self,row):
		rp = self.rp_heroes
		s = db.select([rp]).where(db.and_(rp.c.lobby_id == row['lobby_id'],
						  rp.c.steam_id == row['steam_id']))
		logging.info('executing: {}'.format(s))
		if (result := self.conn.execute(s).fetchall()):
			logging.info('result: {}'.format(result))
			return False
		else:
			logging.info('result: {}'.format(result))
			insert = rp.insert().values(**row)
			logging.info('inserting with: {}'.format(insert))
			result = self.conn.execute(insert)
			return True

	def check_balance(self,discord_id):
		s = db.select([self.balance_ledger.c.tokens]).where(self.balance_ledger.c.discord_id == discord_id)
		if (result := self.conn.execute(s).fetchone()):
			return result[0]
		else:
			insert = self.balance_ledger.insert().values(discord_id=discord_id,tokens=1000)
			result = self.conn.execute(insert)
			return 1000

	def insert_bet(self,match_id,discord_id,side,amount):
		balance = self.check_balance(discord_id)
		if balance < amount:
			return 'insufficent balance'
		else:
			new_balance = balance - amount

			bl = self.bet_ledger
			s = db.select([bl.c.bet_side]).where(bl.c.match_id == match_id)

			if (result := self.conn.execute(s).fetchall()):
				if all(map(lambda y: y == side,map(lambda x: x[0],result))):
					begin_statement = text('''BEGIN WORK;
	LOCK TABLE "Kali".bet_ledger IN ACCESS EXCLUSIVE MODE;
	LOCK TABLE "Kali".balance_ledger IN ACCESS EXCLUSIVE MODE;''')
					bet_insert_template = text('INSERT INTO "Kali".bet_ledger VALUES (:match_id, :gambler_id, :side, :amount, 0)')
					balance_update_template = text('UPDATE "Kali".balance_ledger SET tokens = :tokens WHERE discord_id = :discord_id')
					end_statement = text('COMMIT WORK;')
					insert_statement = str(bet_insert_template.bindparams(match_id = match_id,gambler_id = discord_id, side = side, amount = amount).compile(compile_kwargs={"literal_binds": True}))+';'
					update_statement = str(balance_update_template.bindparams(tokens = new_balance,discord_id = discord_id).compile(compile_kwargs={"literal_binds": True}))+';'
					raw_dogg_sql = '''{}
{}
{}
{} '''
					raw_dogg_sql = raw_dogg_sql.format(begin_statement,insert_statement,update_statement,end_statement)
					print(raw_dogg_sql)
					result = self.conn.execute(raw_dogg_sql)
					return 'bet {} CURRENCY on {} win for match id {}'.format(amount,side,match_id)
				else:
					return 'cannot bet on both sides'
			else:
				begin_statement = text('''BEGIN WORK;
LOCK TABLE "Kali".bet_ledger IN ACCESS EXCLUSIVE MODE;
LOCK TABLE "Kali".balance_ledger IN ACCESS EXCLUSIVE MODE;''')
				bet_insert_template = text('INSERT INTO "Kali".bet_ledger VALUES (:match_id, :gambler_id, :side, :amount, 0);')
				balance_update_template = text('UPDATE "Kali".balance_ledger SET tokens = :tokens WHERE discord_id = :discord_id;')
				end_statement = text('COMMIT WORK;')
				insert_statement = str(bet_insert_template.bindparams(match_id = match_id,gambler_id = discord_id, side = side, amount = amount).compile(compile_kwargs={"literal_binds": True}))+';'
				update_statement = str(balance_update_template.bindparams(tokens = new_balance,discord_id = discord_id).compile(compile_kwargs={"literal_binds": True}))+';'
				raw_dogg_sql = '''{}
{}
{}
{} '''
				raw_dogg_sql = raw_dogg_sql.format(begin_statement,insert_statement,update_statement,end_statement)
				print(raw_dogg_sql)
				result = self.conn.execute(raw_dogg_sql)
				return 'bet {} CURRENCY on {} win for match id {}'.format(amount,side,match_id)
				

