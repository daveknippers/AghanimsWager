
import discord

import sqlalchemy as db

from tokens import TOKEN, CONNECTION_STRING

import datetime as dt

bot_channel = 'dota-bet'
currency = 'tear'

client = discord.Client()



@client.event
async def on_message(message):

	if message.author == client.user or str(message.channel) != bot_channel:
		return

	if message.content.startswith('!hello'):
		msg = 'oi fuck off {0.author.mention}'.format(message)
		await message.channel.send(msg)

	if message.content.startswith('!balance'):
		amount = pgdb.check_balance(message.author.id)
		if amount != 1:
			msg = '{0.author.mention}, you have {1} {2}s'.format(message,amount,currency)
		else:
			msg = '{0.author.mention}, you have {1} {2}'.format(message,amount,currency)
		await message.channel.send(msg)

	if message.content.startswith('!start-match'):
		try:
			command,side = message.content.lower().split()
			fail = False
		except ValueError:
			fail = True
		if fail or (side != 'radiant' and side != 'dire'):
			msg = '{0.author.mention}, the proper syntax is !start-match radiant/dire'.format(message)
			await message.channel.send(msg)
		else:
			success,start_time = pgdb.start_match(message.author.id,side)
			start_time = start_time.strftime("%Y-%m-%d %H:%M:%S")
			if success:
				msg = '{0.author.mention}, match started at {1}'.format(message,start_time)
			else:
				msg = '{0.author.mention}, wrap up your ongoing match started at {1} with !payout radiant/dire'\
					   .format(message,start_time)
			await message.channel.send(msg)

	if message.content.startswith('!payout'):
		try:
			command,side = message.content.lower().split()
			fail = False
		except ValueError:
			fail = True
		if fail or (side != 'radiant' and side != 'dire'):
			msg = '{0.author.mention}, the proper syntax is !start-match radiant/dire'.format(message)
			await message.channel.send(msg)


	'''
	if message.content.startswith('!bet'):
		try:
			command,player,side = message.content.split()
		except ValueError:
			msg = '{0.author.mention}, the proper syntax is !bet player radiant/dire'.format(message)
			await message.channel.send(msg)
	'''
			
@client.event
async def on_ready():
	print('Logged in as',client.user.name,client.user.id)

class PGDB:

	def __init__(self,conn_string):
		self.engine = db.create_engine(conn_string)
		self.conn = self.engine.connect()
		self.metadata = db.MetaData(schema='Kali')

		self.match_ledger = db.Table('match_ledger', self.metadata,
			db.Column('match_id',db.Integer,db.Sequence('match_seq'),primary_key=True,nullable=False),
			db.Column('discord_id',db.BigInteger,nullable=False),
			db.Column('side',db.String(7),nullable=False),
			db.Column('winning_side',db.String(7),nullable=True),
			db.Column('creation_time',db.DateTime,nullable=False))

		self.bet_ledger = db.Table('bet_ledger', self.metadata,
			db.Column('match_id',db.Integer,nullable=False),
			db.Column('gambler_id',db.BigInteger,nullable=False),
			db.Column('bet_side',db.String(7),nullable=False),
			db.Column('amount',db.BigInteger,nullable=False))

		self.balance_ledger = db.Table('balance_ledger',self.metadata,
			db.Column('discord_id',db.BigInteger,primary_key=True,nullable=False),
			db.Column('tokens',db.BigInteger,nullable=False))

		self.metadata.create_all(self.engine)

	def check_balance(self,discord_id):
		s = db.select([self.balance_ledger]).where(self.balance_ledger.c.discord_id == discord_id)
		if (result := self.conn.execute(s).fetchone()):
			return result.tokens
		else:
			insert = self.balance_ledger.insert().values(discord_id=discord_id,tokens=1000)
			result = self.conn.execute(insert)
			return 1000
	
	def start_match(self,discord_id,side):
		now = dt.datetime.now()
		ml = self.match_ledger
		s = db.select([ml.c.discord_id,ml.c.winning_side,ml.c.creation_time])\
			.where(db.and_(	self.match_ledger.c.discord_id == discord_id, 
							self.match_ledger.c.winning_side == None))
		if (result := self.conn.execute(s).fetchone()):
			return False,result.creation_time
		else:
			insert = self.match_ledger.insert().values(discord_id=discord_id,side=side,creation_time=now)
			result = self.conn.execute(insert)
			return True,now

	def set_winner(self,discord_id,winning_side):
		raise NotImplemented
		ml = self.match_ledger
		s = db.select([ml.c.discord_id,ml.c.winning_side,ml.c.creation_time])\
			.where(db.and_(	self.match_ledger.c.discord_id == discord_id, 
							self.match_ledger.c.winning_side == None))
		if (result := self.conn.execute(s).fetchone()):
			return False,result.creation_time
		else:
			insert = self.match_ledger.insert().values(discord_id=discord_id,side=side,creation_time=now)
			result = self.conn.execute(insert)
			return True,now

			
			
			
		


'''
self.bet_ledger = db.Table('bet_ledger',self.metadata,autoload=True,autoload_with=self.engine)
self.balance_ledger = db.Table('balance_ledger',self.metadata,autoload=True,autoload_with=self.engine)
self.match_ledger = db.Table('match_ledger',self.metadata,autoload=True,autoload_with=self.engine)
'''

if __name__ == '__main__':
	pgdb = PGDB(CONNECTION_STRING)
	client.run(TOKEN)

